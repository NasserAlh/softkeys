using System.Runtime.InteropServices;

namespace Softkeys.Native;

/// <summary>
/// A PC/AT set-1 make code plus the E0 extended-key marker (D-05).
/// </summary>
public readonly record struct ScanKey(ushort Code, bool Extended);

/// <summary>
/// Stateless key injection via SendInput using scan codes (D-02, F-03).
/// The active Windows keyboard layout translates scan codes to characters,
/// so EN/AR both work without any layout knowledge here. Zero cross-process
/// calls (NF-01): build the INPUT batches, hand them to SendInput, done.
/// </summary>
public static class InputInjector
{
    internal const uint INPUT_KEYBOARD = 1;

    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    private const int VK_CAPITAL = 0x14;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    /// <summary>
    /// D-09: CapsLock toggle bit via GetKeyState — reads input state
    /// synchronized to this thread's message queue; not a cross-process query,
    /// explicitly permitted per the NF-01 clarification in Amendment A1.
    /// Refresh cadence is event-driven only (D-10).
    /// </summary>
    public static bool IsCapsLockOn => (GetKeyState(VK_CAPITAL) & 0x0001) != 0;

    /// <summary>
    /// CR-18: the pause between the chord's three SendInput calls. 60 ms is
    /// the value proven by the witnessed DF-01 altprobe (split-with-gaps
    /// closed the window; zero-gap did not); a smaller value would need its
    /// own witnessed probe to justify. Two gaps per chord = 120 ms once per
    /// chord — repeats are unmodified (F-05), so repeat taps never pay it.
    /// </summary>
    internal const int ChordGapMs = 60;

    /// <summary>
    /// Injects a full tap. Plain taps are one SendInput call; chords are
    /// three (modifier downs / key tap / modifier ups) with a ChordGapMs
    /// pause between calls (D-03 as amended by CR-18) so targets that sample
    /// modifier state asynchronously at processing time (DF-01, Win11
    /// Notepad) still see the modifier held — the timing a physical hand
    /// produces. Atomicity holds within each call, and the intra-chord sleeps
    /// block our own UI thread, so our next click cannot interleave either.
    /// Returns true when every event was accepted by the system.
    /// </summary>
    public static bool Tap(ScanKey key, IReadOnlyList<ScanKey>? modifiers = null)
    {
        INPUT[][] batches = BuildBatches(key, modifiers ?? Array.Empty<ScanKey>());

        bool ok = true;
        for (int b = 0; b < batches.Length; b++)
        {
            if (b > 0)
                Thread.Sleep(ChordGapMs);
            ok &= SendInput((uint)batches[b].Length, batches[b], Marshal.SizeOf<INPUT>()) == batches[b].Length;
        }
        return ok;
    }

    /// <summary>
    /// Harness/diagnostic only (altprobe): inject a single down or up event
    /// as its own SendInput call. The product path always uses Tap — this
    /// exists so a probe can vary batching/timing, never the keyboard itself.
    /// </summary>
    internal static bool EmitSingle(ScanKey key, bool up)
    {
        var sequence = new[] { KeyEvent(key, up) };
        return SendInput(1, sequence, Marshal.SizeOf<INPUT>()) == 1;
    }

    /// <summary>
    /// D-03 ordering (press mods → press key → release key → release mods,
    /// reverse) split per CR-18: one batch for a plain tap; mod-downs / tap /
    /// mod-ups for a chord. Internal so tests can verify structure, ordering,
    /// and flags without emitting real input.
    /// </summary>
    internal static INPUT[][] BuildBatches(ScanKey key, IReadOnlyList<ScanKey> modifiers)
    {
        INPUT[] tap = { KeyEvent(key, up: false), KeyEvent(key, up: true) };
        if (modifiers.Count == 0)
            return new[] { tap };

        var downs = new INPUT[modifiers.Count];
        var ups = new INPUT[modifiers.Count];
        for (int m = 0; m < modifiers.Count; m++)
        {
            downs[m] = KeyEvent(modifiers[m], up: false);
            ups[modifiers.Count - 1 - m] = KeyEvent(modifiers[m], up: true);
        }
        return new[] { downs, tap, ups };
    }

    private static INPUT KeyEvent(ScanKey key, bool up)
    {
        uint flags = KEYEVENTF_SCANCODE;
        if (key.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (up) flags |= KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0, // must be 0 with KEYEVENTF_SCANCODE
                    wScan = key.Code,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }
}

/// <summary>
/// Scan codes (PC/AT set 1) for every key in the layout (F-04 per CR-03;
/// function row added by F-14/Amendment A1). Key names follow vboard.py so
/// KeyMap maps one-to-one; arrows use Up/Down/Left/Right instead of glyphs.
/// </summary>
public static class ScanCodeTable
{
    public static readonly IReadOnlyDictionary<string, ScanKey> Keys = new Dictionary<string, ScanKey>
    {
        // Row 0 — function row (F-14): standard non-extended codes
        ["Esc"] = new(0x01, false),
        ["F1"] = new(0x3B, false),
        ["F2"] = new(0x3C, false),
        ["F3"] = new(0x3D, false),
        ["F4"] = new(0x3E, false),
        ["F5"] = new(0x3F, false),
        ["F6"] = new(0x40, false),
        ["F7"] = new(0x41, false),
        ["F8"] = new(0x42, false),
        ["F9"] = new(0x43, false),
        ["F10"] = new(0x44, false),
        ["F11"] = new(0x57, false),
        ["F12"] = new(0x58, false),

        // Row 1 — number row
        ["`"] = new(0x29, false),
        ["1"] = new(0x02, false),
        ["2"] = new(0x03, false),
        ["3"] = new(0x04, false),
        ["4"] = new(0x05, false),
        ["5"] = new(0x06, false),
        ["6"] = new(0x07, false),
        ["7"] = new(0x08, false),
        ["8"] = new(0x09, false),
        ["9"] = new(0x0A, false),
        ["0"] = new(0x0B, false),
        ["-"] = new(0x0C, false),
        ["="] = new(0x0D, false),
        ["Backspace"] = new(0x0E, false),

        // Row 2 — QWERTY top
        ["Tab"] = new(0x0F, false),
        ["Q"] = new(0x10, false),
        ["W"] = new(0x11, false),
        ["E"] = new(0x12, false),
        ["R"] = new(0x13, false),
        ["T"] = new(0x14, false),
        ["Y"] = new(0x15, false),
        ["U"] = new(0x16, false),
        ["I"] = new(0x17, false),
        ["O"] = new(0x18, false),
        ["P"] = new(0x19, false),
        ["["] = new(0x1A, false),
        ["]"] = new(0x1B, false),
        ["\\"] = new(0x2B, false),

        // Row 3 — home row
        ["CapsLock"] = new(0x3A, false),
        ["A"] = new(0x1E, false),
        ["S"] = new(0x1F, false),
        ["D"] = new(0x20, false),
        ["F"] = new(0x21, false),
        ["G"] = new(0x22, false),
        ["H"] = new(0x23, false),
        ["J"] = new(0x24, false),
        ["K"] = new(0x25, false),
        ["L"] = new(0x26, false),
        [";"] = new(0x27, false),
        ["'"] = new(0x28, false),
        ["Enter"] = new(0x1C, false),

        // Row 4 — bottom letter row (↑ lives here per CR-03)
        ["Shift_L"] = new(0x2A, false),
        ["Z"] = new(0x2C, false),
        ["X"] = new(0x2D, false),
        ["C"] = new(0x2E, false),
        ["V"] = new(0x2F, false),
        ["B"] = new(0x30, false),
        ["N"] = new(0x31, false),
        ["M"] = new(0x32, false),
        [","] = new(0x33, false),
        ["."] = new(0x34, false),
        ["/"] = new(0x35, false),
        ["Shift_R"] = new(0x36, false),
        ["Up"] = new(0x48, true),

        // Row 5 — modifier row (← → ↓ live here per CR-03)
        ["Ctrl_L"] = new(0x1D, false),
        ["Super_L"] = new(0x5B, true),
        ["Alt_L"] = new(0x38, false),
        ["Space"] = new(0x39, false),
        ["Alt_R"] = new(0x38, true),
        ["Super_R"] = new(0x5C, true),
        ["Ctrl_R"] = new(0x1D, true),
        ["Left"] = new(0x4B, true),
        ["Right"] = new(0x4D, true),
        ["Down"] = new(0x50, true),
    };
}
