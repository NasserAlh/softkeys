using System.Runtime.InteropServices;

namespace Softkeys.Native;

/// <summary>
/// Window-style plumbing for F-01 (capture exclusion), F-02 (no focus steal),
/// and F-08 (topmost asserted once at startup, never re-asserted). Consumed by
/// MainWindow at M2; D-04 dictates ApplyNoActivate runs in OnSourceInitialized,
/// the earliest point where the HWND exists.
/// </summary>
public static class WindowStyles
{
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_MAXIMIZEBOX = 0x00010000;
    internal const long WS_EX_TOPMOST = 0x00000008;
    internal const long WS_EX_NOACTIVATE = 0x08000000;

    internal const uint WDA_NONE = 0x00;
    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    /// <summary>
    /// F-02: the window never takes activation, so the target application
    /// keeps focus for every click. WS_EX_TOPMOST is set alongside per F-02's
    /// style pairing; z-order insertion still happens once via ApplyTopmostOnce.
    /// </summary>
    public static void ApplyNoActivate(IntPtr hwnd)
    {
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_NOACTIVATE | WS_EX_TOPMOST));
    }

    /// <summary>
    /// F-08: assert HWND_TOPMOST exactly once at startup — no re-assertion
    /// timers, no z-order polling.
    /// </summary>
    public static bool ApplyTopmostOnce(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>
    /// F-20 (D-20): clearing WS_MAXIMIZEBOX withdraws the window from shell
    /// Snap participation (edge/corner snap, Snap Layouts, Win+arrow), so
    /// dragging against a screen edge just moves it. Resize capability is a
    /// separate style bit and keeps the CR-12 edge band working — verified by
    /// T-18, not assumed.
    /// </summary>
    public static void RemoveMaximizeCapability(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style & ~WS_MAXIMIZEBOX));
    }

    /// <summary>
    /// F-01: when excluded, the window is absent from screenshots and
    /// recordings (WDA_EXCLUDEFROMCAPTURE). Returns false if the system
    /// refuses (e.g. unsupported compositor state) so the caller can surface
    /// the toggle state honestly.
    /// </summary>
    public static bool SetCaptureExclusion(IntPtr hwnd, bool excluded) =>
        SetWindowDisplayAffinity(hwnd, excluded ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
}
