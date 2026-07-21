using System.IO;

namespace Softkeys.Tests;

/// <summary>
/// F-10 behavior, headless: missing/corrupt/wrong-typed settings files fall
/// back to defaults silently; a save/load roundtrip preserves every field.
/// </summary>
internal static class SettingsTests
{
    private static int _passed;
    private static int _failed;

    public static int RunAll()
    {
        Console.WriteLine();

        string dir = Path.Combine(Path.GetTempPath(), "softkeys-settings-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string missing = Path.Combine(dir, "missing.json");
            Settings defaults = Settings.Load(missing);
            Check("missing file yields defaults",
                defaults is { BackgroundColor: "Black", Opacity: 0.90, WindowWidth: 900, WindowHeight: 320, CaptureExcluded: true });

            string corrupt = Path.Combine(dir, "corrupt.json");
            File.WriteAllText(corrupt, "{ this is not json !!");
            Check("corrupt file yields defaults silently", Settings.Load(corrupt).BackgroundColor == "Black");

            string empty = Path.Combine(dir, "empty.json");
            File.WriteAllText(empty, "");
            Check("empty file yields defaults silently", Settings.Load(empty).Opacity == 0.90);

            string wrongTypes = Path.Combine(dir, "wrongtypes.json");
            File.WriteAllText(wrongTypes, "{\"Opacity\": \"very\", \"WindowWidth\": []}");
            Check("wrong-typed fields yield defaults silently", Settings.Load(wrongTypes).WindowWidth == 900);

            string nullLiteral = Path.Combine(dir, "null.json");
            File.WriteAllText(nullLiteral, "null");
            Check("json null yields defaults", Settings.Load(nullLiteral).CaptureExcluded);

            string roundtrip = Path.Combine(dir, "sub", "roundtrip.json");
            var custom = new Settings
            {
                BackgroundColor = "Teal",
                Opacity = 0.55,
                WindowWidth = 1234,
                WindowHeight = 456,
                CaptureExcluded = false,
            };
            custom.Save(roundtrip);
            Settings restored = Settings.Load(roundtrip);
            Check("roundtrip preserves all fields",
                restored is { BackgroundColor: "Teal", Opacity: 0.55, WindowWidth: 1234, WindowHeight: 456, CaptureExcluded: false });
            Check("save creates missing directories", File.Exists(roundtrip));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed;
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {name}");
        }
    }
}
