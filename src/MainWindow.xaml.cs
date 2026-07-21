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
    private Brush _armedBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

    /// <summary>A keycap whose Latin text changes at runtime (F-06/F-13).</summary>
    private sealed record DynamicCap(KeyDef Key, TextBlock Latin);

    private readonly List<DynamicCap> _dynamicCaps = new();
    private Button? _capsLockButton;

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

        // D-10: pointer-enter is one of the two CapsLock refresh triggers.
        MouseEnter += (_, _) => RefreshKeyCaps();

        _initialized = true;
        ApplyAppearance();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // D-04: earliest point where the HWND exists.
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.ApplyNoActivate(_hwnd);
        WindowStyles.RemoveMaximizeCapability(_hwnd); // F-20 snap immunity (D-20)
        WindowStyles.ApplyTopmostOnce(_hwnd);

        // F-01 per persisted setting (default ON); the checkbox reflects the
        // actual SetWindowDisplayAffinity result so it never lies.
        if (_settings.CaptureExcluded)
            CaptureToggle.IsChecked = WindowStyles.SetCaptureExclusion(_hwnd, excluded: true);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        // CR-13 as narrowed by D-19: an OSK has no meaningful maximized state,
        // so Maximized reverts; Minimized is a legal state (F-19) and restore
        // is the user's taskbar click.
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        base.OnStateChanged(e);
    }

    // -- Borderless resize (F-11) -------------------------------------------

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const double ResizeBand = 8.0; // DIPs; PMv2 conversion via PointFromScreen

    /// <summary>
    /// In-process subclass of our own HWND only (HwndSource.AddHook) — not a
    /// window hook in the SetWindowsHookEx/NF-01 sense; no other process is
    /// ever touched. Maps the borderless window's edge band to system resize
    /// hit codes and swallows caption double-clicks from DragMove.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST when WindowState == WindowState.Normal:
                int hit = HitTestResizeBand(lParam);
                if (hit != HTCLIENT)
                {
                    handled = true;
                    return new IntPtr(hit);
                }
                break;

            case WM_NCLBUTTONDBLCLK when wParam.ToInt64() == HTCAPTION:
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private int HitTestResizeBand(IntPtr lParam)
    {
        long lp = lParam.ToInt64();
        int screenX = unchecked((short)(lp & 0xFFFF));
        int screenY = unchecked((short)((lp >> 16) & 0xFFFF));
        Point p = PointFromScreen(new Point(screenX, screenY));

        bool left = p.X < ResizeBand;
        bool right = p.X > ActualWidth - ResizeBand;
        bool top = p.Y < ResizeBand;
        bool bottom = p.Y > ActualHeight - ResizeBand;

        if (top && left) return 13;     // HTTOPLEFT
        if (top && right) return 14;    // HTTOPRIGHT
        if (bottom && left) return 16;  // HTBOTTOMLEFT
        if (bottom && right) return 17; // HTBOTTOMRIGHT
        if (left) return 10;            // HTLEFT
        if (right) return 11;           // HTRIGHT
        if (top) return 12;             // HTTOP
        if (bottom) return 15;          // HTBOTTOM
        return HTCLIENT;
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
        // D-13: rows are independent star grids inside one six-row outer grid,
        // so per-row unit sums may differ (27 for the F-row, 30 elsewhere)
        // while row heights stay equal and widths scale proportionally (F-11).
        for (int r = 0; r < KeyMap.Rows.Count; r++)
            KeyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (int r = 0; r < KeyMap.Rows.Count; r++)
        {
            var rowGrid = new Grid();
            int units = KeyMap.Rows[r].Sum(k => k.Width);
            for (int c = 0; c < units; c++)
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(rowGrid, r);
            KeyGrid.Children.Add(rowGrid);

            int col = 0;
            foreach (KeyDef key in KeyMap.Rows[r])
            {
                Button button = CreateKeyButton(key);
                Grid.SetColumn(button, col);
                Grid.SetColumnSpan(button, key.Width);
                rowGrid.Children.Add(button);
                col += key.Width;
            }
        }
    }

    private Button CreateKeyButton(KeyDef key)
    {
        var button = new Button { Tag = key, Margin = new Thickness(2) };

        // R-03: press/release/leave triple, mirroring vboard's handlers.
        button.PreviewMouseLeftButtonDown += OnKeyPress;
        button.PreviewMouseLeftButtonUp += OnKeyRelease;
        button.MouseLeave += OnKeyLeave;

        if (key.IsModifier)
            _modifierButtons[key.Name] = button;
        if (key.Name == "CapsLock")
            _capsLockButton = button;

        bool dynamic = IsLetter(key) || key.ShiftLabel is not null;
        if (!dynamic && key.Arabic is null)
        {
            button.Content = key.Label;
            return button;
        }

        // D-12: structured cap — Latin TextBlock, plus the Arabic glyph
        // bottom-right at ~75% size for dual-script keys (F-12). Explicit
        // KeyForeground references keep the F-09 contrast switch working
        // (the implicit TextBlock style would otherwise paint these as
        // header text).
        var content = new Grid();
        var latin = new TextBlock { Text = InitialLatin(key) };
        latin.SetResourceReference(TextBlock.ForegroundProperty, "KeyForeground");

        if (key.Arabic is not null)
        {
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Stretch;
            latin.HorizontalAlignment = HorizontalAlignment.Left;
            latin.VerticalAlignment = VerticalAlignment.Top;
            latin.Margin = new Thickness(4, 1, 0, 0);

            var arabic = new TextBlock
            {
                Text = key.Arabic,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 1),
            };
            arabic.SetResourceReference(TextBlock.ForegroundProperty, "KeyForeground");
            content.Children.Add(arabic);
        }
        else
        {
            latin.HorizontalAlignment = HorizontalAlignment.Center;
            latin.VerticalAlignment = VerticalAlignment.Center;
        }

        content.Children.Add(latin);
        button.Content = content;

        if (dynamic)
            _dynamicCaps.Add(new DynamicCap(key, latin));

        return button;
    }

    private static bool IsLetter(KeyDef key) =>
        key.Name.Length == 1 && key.Name[0] is >= 'A' and <= 'Z';

    private static string InitialLatin(KeyDef key) =>
        IsLetter(key) ? key.Label.ToLowerInvariant() : key.Label;

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

        RefreshKeyCaps();
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

        // D-10: after-each-emit is the other CapsLock refresh trigger. The
        // immediate refresh updates shift labels; the queued one re-reads
        // CapsLock after this input cycle's messages have drained.
        RefreshKeyCaps();
        Dispatcher.BeginInvoke(RefreshKeyCaps, DispatcherPriority.Background);
    }

    /// <summary>
    /// F-06 + F-13: symbol caps show their shifted glyph while Shift is armed;
    /// letter caps are lowercase unless Shift armed XOR CapsLock active; the
    /// CapsLock key carries the armed-style highlight while active (D-09).
    /// </summary>
    private void RefreshKeyCaps()
    {
        bool shifted = _armed["Shift_L"] || _armed["Shift_R"];
        bool caps = InputInjector.IsCapsLockOn;

        foreach ((KeyDef key, TextBlock latin) in _dynamicCaps)
        {
            if (IsLetter(key))
                latin.Text = shifted ^ caps ? key.Label : key.Label.ToLowerInvariant();
            else if (key.ShiftLabel is not null)
                latin.Text = shifted ? key.ShiftLabel : key.Label;
        }

        if (_capsLockButton is not null)
            _capsLockButton.Background = caps ? _armedBrush : Brushes.Transparent;
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

        RefreshKeyCaps(); // re-tint the CapsLock highlight with the new brush
    }

    private void OnDragBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Single clicks only: a double-click would reach the OS as a caption
        // double-click (DragMove reports HTCAPTION) and toggle maximize.
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
            DragMove();
    }

    // F-19: standard taskbar minimize; NOACTIVATE/topmost/capture styles are
    // window styles and persist across the minimize/restore round trip.
    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
