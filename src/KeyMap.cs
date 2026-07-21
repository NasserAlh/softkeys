namespace Softkeys;

/// <summary>
/// One key of the layout: <paramref name="Name"/> indexes ScanCodeTable,
/// <paramref name="Label"/> is the resting cap text, <paramref name="ShiftLabel"/>
/// the cap shown while Shift is armed (F-06, null when unchanged),
/// <paramref name="Width"/> the star-unit span (D-01, D-13),
/// <paramref name="IsModifier"/> marks the eight sticky latches (F-05),
/// <paramref name="Arabic"/> the base Arabic (101) glyph for dual-script caps
/// (F-12, null for keys identical in both layouts). Display-layer only —
/// Arabic glyphs never influence injection (D-02 owns that).
/// </summary>
public sealed record KeyDef(
    string Name, string Label, string? ShiftLabel, int Width, bool IsModifier, string? Arabic = null);

/// <summary>
/// The six rows of the v1.1 layout: the F-14 function row above the five
/// vboard.py rows (F-04 per CR-03). Rows are independent star grids: the
/// function row sums to 27 units (Esc 3 + 12×2), the five main rows to 30.
/// Arabic glyphs follow the standard Windows Arabic (101) layout, keyed by
/// scan-code position (F-12); base glyphs only (L-05).
/// </summary>
public static class KeyMap
{
    private static KeyDef K(string name, int width = 2) => new(name, name, null, width, false);
    private static KeyDef Ar(string name, string arabic, int width = 2) => new(name, name, null, width, false, arabic);
    private static KeyDef Sym(string name, string shift, int width = 2) => new(name, name, shift, width, false);
    private static KeyDef SymAr(string name, string shift, string arabic, int width = 2) => new(name, name, shift, width, false, arabic);
    private static KeyDef Mod(string name, string label, int width = 2) => new(name, label, null, width, true);

    public static readonly IReadOnlyList<IReadOnlyList<KeyDef>> Rows = new[]
    {
        // Row 0 — function row (F-14): equal widths, Esc wider
        new[]
        {
            K("Esc", 3),
            K("F1"), K("F2"), K("F3"), K("F4"), K("F5"), K("F6"),
            K("F7"), K("F8"), K("F9"), K("F10"), K("F11"), K("F12"),
        },

        // Row 1 — number row (digits are identical in Arabic (101): single label)
        new[]
        {
            SymAr("`", "~", "ذ", 1),
            Sym("1", "!"), Sym("2", "@"), Sym("3", "#"), Sym("4", "$"), Sym("5", "%"),
            Sym("6", "^"), Sym("7", "&"), Sym("8", "*"), Sym("9", "("), Sym("0", ")"),
            Sym("-", "_"), Sym("=", "+"),
            K("Backspace", 5),
        },

        // Row 2 — QWERTY top
        new[]
        {
            K("Tab"),
            Ar("Q", "ض"), Ar("W", "ص"), Ar("E", "ث"), Ar("R", "ق"), Ar("T", "ف"),
            Ar("Y", "غ"), Ar("U", "ع"), Ar("I", "ه"), Ar("O", "خ"), Ar("P", "ح"),
            SymAr("[", "{", "ج"), SymAr("]", "}", "د"),
            Sym("\\", "|", 4),
        },

        // Row 3 — home row
        new[]
        {
            K("CapsLock", 3),
            Ar("A", "ش"), Ar("S", "س"), Ar("D", "ي"), Ar("F", "ب"), Ar("G", "ل"),
            Ar("H", "ا"), Ar("J", "ت"), Ar("K", "ن"), Ar("L", "م"),
            SymAr(";", ":", "ك"), SymAr("'", "\"", "ط"),
            K("Enter", 5),
        },

        // Row 4 — bottom letter row (↑ lives here per CR-03)
        new[]
        {
            Mod("Shift_L", "Shift", 4),
            Ar("Z", "ئ"), Ar("X", "ء"), Ar("C", "ؤ"), Ar("V", "ر"), Ar("B", "لا"),
            Ar("N", "ى"), Ar("M", "ة"),
            SymAr(",", "<", "و"), SymAr(".", ">", "ز"), SymAr("/", "?", "ظ"),
            Mod("Shift_R", "Shift", 4),
            new KeyDef("Up", "↑", null, 2, false),
        },

        // Row 5 — modifier row (← → ↓ live here per CR-03)
        new[]
        {
            Mod("Ctrl_L", "Ctrl"),
            Mod("Super_L", "Super"),
            Mod("Alt_L", "Alt"),
            K("Space", 12),
            Mod("Alt_R", "Alt"),
            Mod("Super_R", "Super"),
            Mod("Ctrl_R", "Ctrl"),
            new KeyDef("Left", "←", null, 2, false),
            new KeyDef("Right", "→", null, 2, false),
            new KeyDef("Down", "↓", null, 2, false),
        },
    };
}
