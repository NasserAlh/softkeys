using System.Runtime.InteropServices;

namespace Softkeys;

/// <summary>
/// Single entry point (CR-04): no args launches the WPF keyboard; --selftest
/// and --harness are console diagnostic modes. The exe is WinExe so the
/// keyboard never opens a console window; console modes reuse inherited std
/// handles when present (piped/redirected), else attach to the parent
/// console, else allocate one.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        return args.FirstOrDefault() switch
        {
            null => RunApp(),
            "--selftest" => WithConsole(() =>
                Tests.InputInjectorTests.RunAll() + Tests.KeyMapTests.RunAll() + Tests.SettingsTests.RunAll()),
            "--harness" => WithConsole(Harness.Run),
            "--capsprobe" => WithConsole(Harness.RunCapsProbe),
            "--altprobe" => WithConsole(Harness.RunAltProbe),
            _ => WithConsole(Usage),
        };
    }

    private static int RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static int Usage()
    {
        Console.WriteLine("softkeys — on-screen keyboard");
        Console.WriteLine();
        Console.WriteLine("usage:");
        Console.WriteLine("  softkeys              launch the keyboard");
        Console.WriteLine("  softkeys --selftest   run struct-layout and scan-code table tests (D-08)");
        Console.WriteLine("  softkeys --harness    launch Notepad and type \"test\" via SendInput");
        return 2;
    }

    private static int WithConsole(Func<int> mode)
    {
        if (!HasStdOutput() && !AttachConsole(ATTACH_PARENT_PROCESS))
            AllocConsole();
        return mode();
    }

    private static bool HasStdOutput()
    {
        IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
        return handle != IntPtr.Zero && handle != new IntPtr(-1);
    }

    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);
    private const int STD_OUTPUT_HANDLE = -11;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
