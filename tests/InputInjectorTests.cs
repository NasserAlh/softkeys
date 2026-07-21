using System.Runtime.InteropServices;
using Softkeys.Native;
using static Softkeys.Native.InputInjector;

namespace Softkeys.Tests;

/// <summary>
/// Self-contained test runner: the SDLC names no test framework and NuGet
/// packages are excluded, so assertions run in-process and the exit code is
/// the failure count. Covers D-08 (x64 struct layouts), scan-code table
/// integrity, and the D-03/D-05 injection sequence.
/// </summary>
internal static class InputInjectorTests
{
    private static int _passed;
    private static int _failed;

    public static int RunAll()
    {
        StructLayoutTests();
        ScanCodeTableTests();
        TapSequenceTests();

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed;
    }

    // D-08: the classic x64 P/Invoke failure mode is a wrong INPUT size —
    // SendInput then rejects the whole array and keys silently vanish (R-01).
    private static void StructLayoutTests()
    {
        Check("process is 64-bit", Environment.Is64BitProcess);
        Check("INPUT is 40 bytes on x64", Marshal.SizeOf<INPUT>() == 40);
        Check("KEYBDINPUT is 24 bytes on x64", Marshal.SizeOf<KEYBDINPUT>() == 24);
        Check("MOUSEINPUT is 32 bytes on x64", Marshal.SizeOf<MOUSEINPUT>() == 32);
        Check("HARDWAREINPUT is 8 bytes", Marshal.SizeOf<HARDWAREINPUT>() == 8);
        Check("union offset within INPUT is 8 on x64",
            Marshal.OffsetOf<INPUT>(nameof(INPUT.U)).ToInt64() == 8);
    }

    private static void ScanCodeTableTests()
    {
        IReadOnlyDictionary<string, ScanKey> table = ScanCodeTable.Keys;

        // The five v1 rows per F-04 + CR-03 (vboard.py arrangement authoritative).
        string[][] layoutRows =
        {
            new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "Backspace" },
            new[] { "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" },
            new[] { "CapsLock", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", "Enter" },
            new[] { "Shift_L", "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "Shift_R", "Up" },
            new[] { "Ctrl_L", "Super_L", "Alt_L", "Space", "Alt_R", "Super_R", "Ctrl_R", "Left", "Right", "Down" },
        };

        string[] layoutKeys = layoutRows.SelectMany(r => r).ToArray();
        Check("layout defines 64 keys", layoutKeys.Length == 64);
        Check("layout has no duplicate key names", layoutKeys.Distinct().Count() == layoutKeys.Length);

        foreach (string name in layoutKeys)
            Check($"table contains '{name}'", table.ContainsKey(name));

        Check("table has no keys outside the layout", table.Count == layoutKeys.Length);

        Check("all scan codes are non-zero single bytes",
            table.Values.All(k => k.Code is > 0 and <= 0xFF));

        Check("(code, extended) pairs are unique",
            table.Values.Distinct().Count() == table.Count);

        // D-05: exactly the nav/right-side keys carry the E0 extended marker.
        string[] expectedExtended = { "Up", "Down", "Left", "Right", "Ctrl_R", "Alt_R", "Super_L", "Super_R" };
        string[] actualExtended = table.Where(kv => kv.Value.Extended).Select(kv => kv.Key).OrderBy(n => n).ToArray();
        Check("extended-key set matches D-05",
            actualExtended.SequenceEqual(expectedExtended.OrderBy(n => n)));

        // Spot checks against the published set-1 make codes.
        (string Name, ushort Code)[] spots =
        {
            ("`", 0x29), ("1", 0x02), ("0", 0x0B), ("Backspace", 0x0E),
            ("Tab", 0x0F), ("Q", 0x10), ("P", 0x19), ("\\", 0x2B),
            ("CapsLock", 0x3A), ("A", 0x1E), ("Enter", 0x1C),
            ("Shift_L", 0x2A), ("Z", 0x2C), ("/", 0x35), ("Shift_R", 0x36),
            ("Ctrl_L", 0x1D), ("Alt_L", 0x38), ("Space", 0x39),
            ("Up", 0x48), ("Down", 0x50), ("Left", 0x4B), ("Right", 0x4D),
            ("Super_L", 0x5B), ("T", 0x14), ("E", 0x12), ("S", 0x1F),
        };
        foreach ((string name, ushort code) in spots)
            Check($"'{name}' is 0x{code:X2}", table[name].Code == code);
    }

    // D-03: mods down → key down → key up → mods up (reverse), one array.
    private static void TapSequenceTests()
    {
        ScanKey a = ScanCodeTable.Keys["A"];
        ScanKey shift = ScanCodeTable.Keys["Shift_L"];
        ScanKey ctrl = ScanCodeTable.Keys["Ctrl_L"];
        ScanKey up = ScanCodeTable.Keys["Up"];

        INPUT[] plain = BuildTapSequence(a, Array.Empty<ScanKey>());
        Check("plain tap is 2 events", plain.Length == 2);
        Check("plain tap: down then up",
            !IsUp(plain[0]) && IsUp(plain[1]) && plain.All(e => Scan(e) == a.Code));

        INPUT[] combo = BuildTapSequence(a, new[] { ctrl, shift });
        Check("two-modifier tap is 6 events", combo.Length == 6);
        Check("combo order: ctrl↓ shift↓ a↓ a↑ shift↑ ctrl↑",
            Scan(combo[0]) == ctrl.Code && !IsUp(combo[0]) &&
            Scan(combo[1]) == shift.Code && !IsUp(combo[1]) &&
            Scan(combo[2]) == a.Code && !IsUp(combo[2]) &&
            Scan(combo[3]) == a.Code && IsUp(combo[3]) &&
            Scan(combo[4]) == shift.Code && IsUp(combo[4]) &&
            Scan(combo[5]) == ctrl.Code && IsUp(combo[5]));
        Check("all events are keyboard type", combo.All(e => e.type == INPUT_KEYBOARD));
        Check("all events carry KEYEVENTF_SCANCODE",
            combo.All(e => (e.U.ki.dwFlags & KEYEVENTF_SCANCODE) != 0));
        Check("wVk is 0 everywhere (scan-code injection, D-02)",
            combo.All(e => e.U.ki.wVk == 0));

        INPUT[] nav = BuildTapSequence(up, Array.Empty<ScanKey>());
        Check("extended key carries KEYEVENTF_EXTENDEDKEY (D-05)",
            nav.All(e => (e.U.ki.dwFlags & KEYEVENTF_EXTENDEDKEY) != 0));
        Check("non-extended key omits KEYEVENTF_EXTENDEDKEY",
            plain.All(e => (e.U.ki.dwFlags & KEYEVENTF_EXTENDEDKEY) == 0));
    }

    private static ushort Scan(INPUT e) => e.U.ki.wScan;
    private static bool IsUp(INPUT e) => (e.U.ki.dwFlags & KEYEVENTF_KEYUP) != 0;

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
