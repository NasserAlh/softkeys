using Softkeys.Native;

namespace Softkeys.Tests;

/// <summary>
/// KeyMap integrity: the five CR-03 rows, vboard create_row() widths mapped to
/// star units (D-01), the vboard update_label() shift pairs (F-06), and a
/// name-level bijection with the scan-code table.
/// </summary>
internal static class KeyMapTests
{
    private static int _passed;
    private static int _failed;

    public static int RunAll()
    {
        Console.WriteLine();

        IReadOnlyList<IReadOnlyList<KeyDef>> rows = KeyMap.Rows;
        KeyDef[] all = rows.SelectMany(r => r).ToArray();

        Check("five rows (F-04)", rows.Count == 5);
        for (int i = 0; i < rows.Count; i++)
            Check($"row {i + 1} spans 30 star units", rows[i].Sum(k => k.Width) == 30);

        Check("64 keys total", all.Length == 64);
        Check("key names unique", all.Select(k => k.Name).Distinct().Count() == all.Length);
        Check("every key resolves in ScanCodeTable",
            all.All(k => ScanCodeTable.Keys.ContainsKey(k.Name)));
        Check("every ScanCodeTable entry appears in KeyMap",
            ScanCodeTable.Keys.Count == all.Length);

        // F-05: exactly the eight sticky latches.
        string[] expectedMods = { "Shift_L", "Shift_R", "Ctrl_L", "Ctrl_R", "Alt_L", "Alt_R", "Super_L", "Super_R" };
        Check("modifier set matches F-05",
            all.Where(k => k.IsModifier).Select(k => k.Name).OrderBy(n => n)
               .SequenceEqual(expectedMods.OrderBy(n => n)));

        // F-06: the 21 shifted-symbol pairs from vboard update_label().
        (string Name, string Shift)[] pairs =
        {
            ("`", "~"), ("1", "!"), ("2", "@"), ("3", "#"), ("4", "$"), ("5", "%"),
            ("6", "^"), ("7", "&"), ("8", "*"), ("9", "("), ("0", ")"), ("-", "_"),
            ("=", "+"), ("[", "{"), ("]", "}"), ("\\", "|"), (";", ":"), ("'", "\""),
            (",", "<"), (".", ">"), ("/", "?"),
        };
        Check("exactly 21 keys carry shift labels", all.Count(k => k.ShiftLabel is not null) == pairs.Length);
        foreach ((string name, string shift) in pairs)
            Check($"'{name}' shifts to '{shift}'",
                all.Single(k => k.Name == name).ShiftLabel == shift);

        // vboard create_row() widths.
        (string Name, int Width)[] widths =
        {
            ("Space", 12), ("CapsLock", 3), ("Shift_L", 4), ("Shift_R", 4),
            ("Backspace", 5), ("Enter", 5), ("`", 1), ("\\", 4), ("Q", 2), ("Ctrl_L", 2),
        };
        foreach ((string name, int width) in widths)
            Check($"'{name}' width {width}", all.Single(k => k.Name == name).Width == width);

        // Labels: modifiers drop the _L/_R suffix; arrows are glyphs.
        Check("modifier labels drop side suffix",
            all.Where(k => k.IsModifier).All(k => !k.Label.Contains('_')));
        Check("arrow labels are glyphs",
            all.Single(k => k.Name == "Up").Label == "↑" &&
            all.Single(k => k.Name == "Left").Label == "←" &&
            all.Single(k => k.Name == "Right").Label == "→" &&
            all.Single(k => k.Name == "Down").Label == "↓");

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
