using System.Windows;
using System.Windows.Interop;
using Softkeys.Native;

namespace Softkeys;

/// <summary>
/// The keyboard window shell (M2): never activates (F-02), topmost asserted
/// once (F-08), excluded from capture by default (F-01).
/// </summary>
public partial class MainWindow : Window
{
    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // D-04: earliest point where the HWND exists.
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.ApplyNoActivate(_hwnd);
        WindowStyles.ApplyTopmostOnce(_hwnd);
        WindowStyles.SetCaptureExclusion(_hwnd, excluded: true);
    }
}
