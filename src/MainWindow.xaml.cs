using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Softkeys.Native;

namespace Softkeys;

/// <summary>
/// The keyboard window shell (M2): never activates (F-02), topmost asserted
/// once (F-08), excluded from capture by default (F-01), background opacity
/// 0.00–1.00 in 0.01 steps (F-09 subset — full header bar arrives at M4).
/// </summary>
public partial class MainWindow : Window
{
    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        ApplyBackgroundOpacity(OpacitySlider.Value);
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

    private void OnKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name }
            && ScanCodeTable.Keys.TryGetValue(name, out ScanKey key))
        {
            InputInjector.Tap(key);
        }
    }

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
