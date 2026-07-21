using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Softkeys.Native;

namespace Softkeys;

/// <summary>
/// M1 gate evidence (§5): launches Notepad, waits for it to take the
/// foreground on its own (no SetForegroundWindow — we never steal or assign
/// focus), then types "test" through InputInjector. A focus guard refuses to
/// inject unless the foreground window belongs to Notepad, so keystrokes
/// cannot land in another application. The guard's foreground query is
/// harness-only code; the product input path (InputInjector) stays free of
/// any window queries per NF-01.
/// </summary>
internal static class Harness
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static int Run()
    {
        Console.WriteLine("softkeys M1 harness — types \"test\" into Notepad via SendInput.");

        HashSet<int> preexisting = NotepadProcesses().Select(p => p.Id).ToHashSet();

        try
        {
            Process.Start("notepad.exe");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not launch Notepad: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Waiting up to 10 s for Notepad to take focus");
        Console.WriteLine("(click the Notepad window if it does not come to the front by itself)...");
        string? foreground = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            Thread.Sleep(250);
            foreground = ForegroundProcessName();
            if (IsNotepad(foreground))
                break;
        }

        if (!IsNotepad(foreground))
        {
            Console.Error.WriteLine(
                $"Foreground window is '{foreground ?? "unknown"}', not Notepad — refusing to inject.");
            CloseSpawnedNotepad(preexisting);
            return 1;
        }

        foreach (char c in "test")
        {
            ScanKey key = ScanCodeTable.Keys[char.ToUpperInvariant(c).ToString()];
            bool sent = InputInjector.Tap(key);
            Console.WriteLine($"  '{c}'  scan 0x{key.Code:X2}  {(sent ? "sent" : "FAILED")}");
            if (!sent)
            {
                Console.Error.WriteLine("SendInput rejected the event batch.");
                return 1;
            }
            Thread.Sleep(50);
        }

        Console.WriteLine("Done — \"test\" should now be visible in the Notepad window.");
        return 0;
    }

    /// <summary>
    /// Diagnostic for the D-09 open question: does GetKeyState(VK_CAPITAL)
    /// track physical CapsLock toggles on a thread whose NOACTIVATE window
    /// never receives keyboard messages? Prints the read every 250 ms for
    /// 15 s while the tester toggles CapsLock on the physical keyboard.
    /// The timer lives ONLY in this diagnostic — the product runtime stays
    /// timer-free per F-08/D-10.
    /// </summary>
    public static int RunCapsProbe()
    {
        Console.WriteLine("capsprobe: reading GetKeyState(VK_CAPITAL) from a NOACTIVATE WPF window thread.");
        Console.WriteLine("Toggle CapsLock on the PHYSICAL keyboard and watch whether the value follows.");
        Console.WriteLine("Runs for 15 seconds.");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window
        {
            Title = "softkeys capsprobe",
            Width = 240,
            Height = 90,
            ShowActivated = false,
            Focusable = false,
            Content = "capsprobe running — watch the console",
        };
        window.SourceInitialized += (_, _) =>
            WindowStyles.ApplyNoActivate(new WindowInteropHelper(window).Handle);

        bool? last = null;
        int ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            bool caps = InputInjector.IsCapsLockOn;
            if (caps != last)
            {
                Console.WriteLine($"  t={ticks * 250,5} ms  CapsLock={caps}");
                last = caps;
            }
            if (++ticks >= 60)
            {
                timer.Stop();
                app.Shutdown();
            }
        };
        timer.Start();
        window.Show();
        app.Run();
        Console.WriteLine("capsprobe done. If the value never followed your physical toggles,");
        Console.WriteLine("GetKeyState is stale on this thread and the D-09 fallback applies.");
        return 0;
    }

    private static bool IsNotepad(string? processName) =>
        string.Equals(processName, "notepad", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Process> NotepadProcesses() =>
        Process.GetProcesses().Where(p =>
            string.Equals(p.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Best-effort cleanup on abort: gracefully close (WM_CLOSE, never Kill)
    /// Notepad instances that did not exist before we launched one. The Win11
    /// notepad.exe stub hands off to the Store app, so the Process returned by
    /// Process.Start is useless for this; a PID diff finds the real instance.
    /// If Notepad merged into an existing window as a tab, no new process
    /// exists and nothing is closed — acceptable for a harness.
    /// </summary>
    private static void CloseSpawnedNotepad(HashSet<int> preexisting)
    {
        foreach (Process p in NotepadProcesses().Where(p => !preexisting.Contains(p.Id)))
        {
            try
            {
                p.CloseMainWindow();
            }
            catch (InvalidOperationException)
            {
                // process exited on its own — nothing to close
            }
        }
    }

    private static string? ForegroundProcessName()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return null;

        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch (ArgumentException)
        {
            return null; // process exited between query and lookup
        }
    }
}
