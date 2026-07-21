using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Softkeys.Native;

namespace Softkeys;

/// <summary>
/// The keyboard window (M3): full five-row grid from KeyMap (F-04, D-01),
/// sticky one-shot modifiers (F-05), shift labels (F-06), auto-repeat with
/// 400 ms delay / 100 ms interval (F-07, D-06). Never activates (F-02),
/// topmost asserted once (F-08), capture-excluded by default (F-01).
/// </summary>
public partial class MainWindow : Window
{
    private IntPtr _hwnd;

    // Injection order for armed modifiers, mirroring vboard's dict order.
    private static readonly string[] ModifierOrder =
        { "Shift_L", "Shift_R", "Ctrl_L", "Ctrl_R", "Alt_L", "Alt_R", "Super_L", "Super_R" };

    private static readonly Brush ArmedBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

    private readonly Dictionary<string, bool> _armed = ModifierOrder.ToDictionary(m => m, _ => false);
    private readonly Dictionary<string, Button> _modifierButtons = new();
    private readonly List<(Button Button, KeyDef Key)> _shiftableButtons = new();

    // F-07 via two DispatcherTimers (D-06): one-shot 400 ms delay, then 100 ms ticks.
    private readonly DispatcherTimer _repeatDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _repeatInterval = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private KeyDef? _repeatKey;

    public MainWindow()
    {
        InitializeComponent();
        ApplyBackgroundOpacity(OpacitySlider.Value);
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
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // D-04: earliest point where the HWND exists.
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.ApplyNoActivate(_hwnd);
        WindowStyles.ApplyTopmostOnce(_hwnd);

        // F-01 default ON; reflect the actual result so the toggle never lies.
        bool excluded = WindowStyles.SetCaptureExclusion(_hwnd, excluded: true);
        CaptureToggle.IsChecked = excluded;
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
            return; // modifiers never inject alone and never repeat
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
        _modifierButtons[name].Background = armed ? ArmedBrush : Brushes.Transparent;
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

    // -- Shell controls (M2) ------------------------------------------------

    private void OnCaptureToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_hwnd == IntPtr.Zero)
            return; // initial IsChecked assignment during XAML load

        WindowStyles.SetCaptureExclusion(_hwnd, CaptureToggle.IsChecked == true);
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RootBorder is null)
            return; // fired while XAML is still loading

        ApplyBackgroundOpacity(e.NewValue);
    }

    private void ApplyBackgroundOpacity(double opacity)
    {
        double stepped = Math.Round(Math.Clamp(opacity, 0.0, 1.0), 2);
        byte alpha = (byte)Math.Round(stepped * 255);
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x00, 0x00, 0x00));
        if (OpacityLabel is not null)
            OpacityLabel.Text = stepped.ToString("0.00");
    }

    private void OnDragBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
