using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Softkeys.Native;

namespace Softkeys;

/// <summary>
/// The keyboard window: full five-row grid from KeyMap (F-04, D-01), sticky
/// one-shot modifiers (F-05), shift labels (F-06), auto-repeat (F-07, D-06),
/// collapsible header bar with palette and contrast switch (F-09), settings
/// persistence (F-10), proportional resize (F-11). Never activates (F-02),
/// topmost asserted once (F-08), capture-excluded by default (F-01).
/// </summary>
public partial class MainWindow : Window
{
    private IntPtr _hwnd;
    private readonly Settings _settings;
    private bool _initialized;

    // vboard's color list, verbatim; the light set gets dark text (F-09).
    private static readonly (string Name, Color Color)[] Palette =
    {
        ("Black", Color.FromRgb(0, 0, 0)),
        ("Red", Color.FromRgb(255, 0, 0)),
        ("Pink", Color.FromRgb(255, 105, 183)),
        ("White", Color.FromRgb(255, 255, 255)),
        ("Green", Color.FromRgb(0, 255, 0)),
        ("Blue", Color.FromRgb(0, 0, 110)),
        ("Gray", Color.FromRgb(128, 128, 128)),
        ("Dark Gray", Color.FromRgb(64, 64, 64)),
        ("Orange", Color.FromRgb(255, 165, 0)),
        ("Yellow", Color.FromRgb(255, 255, 0)),
        ("Purple", Color.FromRgb(128, 0, 128)),
        ("Cyan", Color.FromRgb(0, 255, 255)),
        ("Teal", Color.FromRgb(0, 128, 128)),
        ("Brown", Color.FromRgb(139, 69, 19)),
        ("Gold", Color.FromRgb(255, 215, 0)),
        ("Silver", Color.FromRgb(192, 192, 192)),
        ("Turquoise", Color.FromRgb(64, 224, 208)),
        ("Magenta", Color.FromRgb(255, 0, 255)),
        ("Olive", Color.FromRgb(128, 128, 0)),
        ("Maroon", Color.FromRgb(128, 0, 0)),
        ("Indigo", Color.FromRgb(75, 0, 130)),
        ("Beige", Color.FromRgb(245, 245, 220)),
        ("Lavender", Color.FromRgb(230, 230, 250)),
    };

    private static readonly HashSet<string> LightBackgrounds =
        new(StringComparer.Ordinal) { "White", "Green", "Yellow", "Gold", "Beige", "Lavender" };

    // Injection order for armed modifiers, mirroring vboard's dict order.
    private static readonly string[] ModifierOrder =
        { "Shift_L", "Shift_R", "Ctrl_L", "Ctrl_R", "Alt_L", "Alt_R", "Super_L", "Super_R" };

    private readonly Dictionary<string, bool> _armed = ModifierOrder.ToDictionary(m => m, _ => false);
    private readonly Dictionary<string, Button> _modifierButtons = new();
    private readonly List<(Button Button, KeyDef Key)> _shiftableButtons = new();
    private Brush _armedBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

    // F-07 via two DispatcherTimers (D-06): one-shot 400 ms delay, then 100 ms ticks.
    private readonly DispatcherTimer _repeatDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _repeatInterval = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private KeyDef? _repeatKey;

    public MainWindow()
    {
        _settings = Settings.Load();
        InitializeComponent();

        Width = Math.Clamp(_settings.WindowWidth, MinWidth, SystemParameters.VirtualScreenWidth);
        Height = Math.Clamp(_settings.WindowHeight, MinHeight, SystemParameters.VirtualScreenHeight);

        foreach ((string name, _) in Palette)
            PaletteCombo.Items.Add(name);
        if (!Palette.Any(p => p.Name == _settings.BackgroundColor))
            _settings.BackgroundColor = "Black";
        PaletteCombo.SelectedItem = _settings.BackgroundColor;

        _settings.Opacity = Math.Round(Math.Clamp(_settings.Opacity, 0.0, 1.0), 2);
        OpacitySlider.Value = _settings.Opacity;
        CaptureToggle.IsChecked = _settings.CaptureExcluded;

        BuildKeyGrid();

        _repeatDelay.Tick += (_, _) =>
        {
            _repeatDelay.Stop();
            if (_repeatKey is not null)
                _repeatInterval.Start();
        };
        _repeatInterval.Tick += (_, _) =>
        {
            if (_repeatKey is { } key)
                EmitKey(key);
            else
                _repeatInterval.Stop();
        };

        _initialized = true;
        ApplyAppearance();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // D-04: earliest point where the HWND exists.
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.ApplyNoActivate(_hwnd);
        WindowStyles.ApplyTopmostOnce(_hwnd);

        // F-01 per persisted setting (default ON); the checkbox reflects the
        // actual SetWindowDisplayAffinity result so it never lies.
        if (_settings.CaptureExcluded)
            CaptureToggle.IsChecked = WindowStyles.SetCaptureExclusion(_hwnd, excluded: true);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.Save(); // F-10; color/opacity/toggle already tracked live
        base.OnClosing(e);
    }

    private void BuildKeyGrid()
    {
        for (int c = 0; c < 30; c++)
            KeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < KeyMap.Rows.Count; r++)
            KeyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (int r = 0; r < KeyMap.Rows.Count; r++)
        {
            int col = 0;
            foreach (KeyDef key in KeyMap.Rows[r])
            {
                var button = new Button { Content = key.Label, Tag = key, Margin = new Thickness(2) };

                // R-03: press/release/leave triple, mirroring vboard's handlers.
                button.PreviewMouseLeftButtonDown += OnKeyPress;
                button.PreviewMouseLeftButtonUp += OnKeyRelease;
                button.MouseLeave += OnKeyLeave;

                Grid.SetRow(button, r);
                Grid.SetColumn(button, col);
                Grid.SetColumnSpan(button, key.Width);
                KeyGrid.Children.Add(button);

                if (key.IsModifier)
                    _modifierButtons[key.Name] = button;
                if (key.ShiftLabel is not null)
                    _shiftableButtons.Add((button, key));

                col += key.Width;
            }
        }
    }

    // -- Key press / sticky modifiers (F-05) --------------------------------

    private void OnKeyPress(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: KeyDef key })
            return;

        if (key.IsModifier)
        {
            ToggleModifier(key.Name);
            return; // modifiers never inject alone (L-04) and never repeat
        }

        EmitKey(key);

        _repeatKey = key;
        _repeatDelay.Stop();
        _repeatDelay.Start();
    }

    private void OnKeyRelease(object sender, MouseButtonEventArgs e) => CancelRepeat();

    private void OnKeyLeave(object sender, MouseEventArgs e) => CancelRepeat();

    private void CancelRepeat()
    {
        _repeatKey = null;
        _repeatDelay.Stop();
        _repeatInterval.Stop();
    }

    private void ToggleModifier(string name)
    {
        SetModifier(name, !_armed[name]);

        // vboard rule: arming both shifts disarms both (F-05 mutual exclusion).
        if (_armed["Shift_L"] && _armed["Shift_R"])
        {
            SetModifier("Shift_L", false);
            SetModifier("Shift_R", false);
        }

        UpdateShiftLabels();
    }

    private void SetModifier(string name, bool armed)
    {
        _armed[name] = armed;
        _modifierButtons[name].Background = armed ? _armedBrush : Brushes.Transparent;
    }

    /// <summary>
    /// One tap: armed modifiers + key in a single SendInput call (D-03), then
    /// the one-shot latches release (F-05) and labels revert (F-06).
    /// Repeat ticks re-enter here after the latches cleared, so repeats are
    /// unmodified — same as vboard.
    /// </summary>
    private void EmitKey(KeyDef key)
    {
        var mods = new List<ScanKey>();
        foreach (string name in ModifierOrder)
            if (_armed[name])
                mods.Add(ScanCodeTable.Keys[name]);

        InputInjector.Tap(ScanCodeTable.Keys[key.Name], mods);

        if (mods.Count > 0)
            foreach (string name in ModifierOrder)
                if (_armed[name])
                    SetModifier(name, false);

        UpdateShiftLabels();
    }

    private void UpdateShiftLabels()
    {
        bool shifted = _armed["Shift_L"] || _armed["Shift_R"];
        foreach ((Button button, KeyDef key) in _shiftableButtons)
            button.Content = shifted ? key.ShiftLabel : key.Label;
    }

    // -- Header bar (F-09) --------------------------------------------------

    private void OnMenuToggle(object sender, RoutedEventArgs e) =>
        HeaderControls.Visibility = HeaderControls.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void OnPaletteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
            return;

        _settings.BackgroundColor = (string)PaletteCombo.SelectedItem!;
        ApplyAppearance();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
            return;

        _settings.Opacity = Math.Round(Math.Clamp(e.NewValue, 0.0, 1.0), 2); // F-09: 0.01 steps
        ApplyAppearance();
    }

    private void OnCaptureToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_hwnd == IntPtr.Zero)
            return; // initial assignment during construction

        _settings.CaptureExcluded = CaptureToggle.IsChecked == true;
        WindowStyles.SetCaptureExclusion(_hwnd, _settings.CaptureExcluded);
    }

    /// <summary>
    /// F-09: background = palette color at the chosen opacity; text flips to
    /// dark on the light backgrounds (vboard's set) via the dynamic brushes.
    /// </summary>
    private void ApplyAppearance()
    {
        Color bg = Palette.First(p => p.Name == _settings.BackgroundColor).Color;
        byte alpha = (byte)Math.Round(_settings.Opacity * 255);
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        OpacityLabel.Text = _settings.Opacity.ToString("0.00", CultureInfo.InvariantCulture);

        bool light = LightBackgrounds.Contains(_settings.BackgroundColor);
        Color text = light ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Colors.White;
        Color header = light ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xCC, 0xCC, 0xCC);
        Resources["KeyForeground"] = new SolidColorBrush(text);
        Resources["HeaderForeground"] = new SolidColorBrush(header);

        _armedBrush = new SolidColorBrush(Color.FromArgb(0x55, text.R, text.G, text.B));
        foreach ((string name, Button button) in _modifierButtons)
            button.Background = _armed[name] ? _armedBrush : Brushes.Transparent;
    }

    private void OnDragBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
