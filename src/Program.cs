namespace Softkeys;

/// <summary>
/// Console entry point for M1: self-tests and the Notepad injection harness.
/// Becomes the WPF bootstrap at M2 (single csproj per §4.1).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        return args.FirstOrDefault() switch
        {
            "--selftest" => Tests.InputInjectorTests.RunAll(),
            "--harness" => Harness.Run(),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.WriteLine("softkeys — M1 native core");
        Console.WriteLine();
        Console.WriteLine("usage:");
        Console.WriteLine("  softkeys --selftest   run struct-layout and scan-code table tests (D-08)");
        Console.WriteLine("  softkeys --harness    launch Notepad and type \"test\" via SendInput");
        return 2;
    }
}
