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

        Check("six rows (F-04 + F-14)", rows.Count == 6);
        Check("function row spans 27 star units (Esc 3 + 12x2)", rows[0].Sum(k => k.Width) == 27);
        for (int i = 1; i < rows.Count; i++)
            Check($"row {i} spans 30 star units", rows[i].Sum(k => k.Width) == 30);

        Check("77 keys total", all.Length == 77);
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
            ("Esc", 3), ("F5", 2),
        };
        foreach ((string name, int width) in widths)
            Check($"'{name}' width {width}", all.Single(k => k.Name == name).Width == width);

        // F-14: function row shape — 13 plain keys, Esc wider, F1..F12 equal.
        IReadOnlyList<KeyDef> fRow = rows[0];
        Check("function row has 13 keys", fRow.Count == 13);
        Check("function row order Esc, F1..F12",
            fRow.Select(k => k.Name).SequenceEqual(
                new[] { "Esc" }.Concat(Enumerable.Range(1, 12).Select(n => $"F{n}"))));
        Check("F1..F12 share one width", fRow.Skip(1).All(k => k.Width == 2));
        Check("function row keys are plain (no modifier/shift/Arabic)",
            fRow.All(k => k is { IsModifier: false, ShiftLabel: null, Arabic: null }));

        // F-12: dual-script caps — exactly the keys that differ under Arabic (101).
        string[] expectedDual =
        {
            "`",
            "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]",
            "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'",
            "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/",
        };
        string[] actualDual = all.Where(k => !string.IsNullOrEmpty(k.Arabic)).Select(k => k.Name).ToArray();
        Check("exactly 34 dual-script keys", actualDual.Length == 34);
        Check("dual-script key set matches Arabic (101) difference set",
            actualDual.OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(expectedDual.OrderBy(n => n, StringComparer.Ordinal)));
        Check("no dual key has an empty Arabic glyph",
            all.Where(k => k.Arabic is not null).All(k => k.Arabic!.Length > 0));

        // Spot checks against the Windows Arabic (101) layout.
        (string Name, string Glyph)[] arabicSpots =
        {
            ("D", "ي"), ("Q", "ض"), ("B", "لا"), ("`", "ذ"), (";", "ك"), ("/", "ظ"), ("H", "ا"),
        };
        foreach ((string name, string glyph) in arabicSpots)
            Check($"'{name}' Arabic glyph is '{glyph}'", all.Single(k => k.Name == name).Arabic == glyph);

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
