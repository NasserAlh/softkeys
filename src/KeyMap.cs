namespace Softkeys;

/// <summary>
/// One key of the v1 layout: <paramref name="Name"/> indexes ScanCodeTable,
/// <paramref name="Label"/> is the resting cap text, <paramref name="ShiftLabel"/>
/// the cap shown while Shift is armed (F-06, null when unchanged),
/// <paramref name="Width"/> the star-unit span per vboard create_row() (D-01),
/// <paramref name="IsModifier"/> marks the eight sticky latches (F-05).
/// </summary>
public sealed record KeyDef(string Name, string Label, string? ShiftLabel, int Width, bool IsModifier);

/// <summary>
/// The five rows of F-04 in vboard.py's exact arrangement (CR-03): ↑ ends the
/// Shift row, ← → ↓ end the modifier row. Every row sums to 30 star units.
/// </summary>
public static class KeyMap
{
    private static KeyDef K(string name, int width = 2) => new(name, name, null, width, false);
    private static KeyDef Sym(string name, string shift, int width = 2) => new(name, name, shift, width, false);
    private static KeyDef Mod(string name, string label, int width = 2) => new(name, label, null, width, true);

    public static readonly IReadOnlyList<IReadOnlyList<KeyDef>> Rows = new[]
    {
        new[]
        {
            Sym("`", "~", 1),
            Sym("1", "!"), Sym("2", "@"), Sym("3", "#"), Sym("4", "$"), Sym("5", "%"),
            Sym("6", "^"), Sym("7", "&"), Sym("8", "*"), Sym("9", "("), Sym("0", ")"),
            Sym("-", "_"), Sym("=", "+"),
            K("Backspace", 5),
        },
        new[]
        {
            K("Tab"),
            K("Q"), K("W"), K("E"), K("R"), K("T"), K("Y"), K("U"), K("I"), K("O"), K("P"),
            Sym("[", "{"), Sym("]", "}"),
            Sym("\\", "|", 4),
        },
        new[]
        {
            K("CapsLock", 3),
            K("A"), K("S"), K("D"), K("F"), K("G"), K("H"), K("J"), K("K"), K("L"),
            Sym(";", ":"), Sym("'", "\""),
            K("Enter", 5),
        },
        new[]
        {
            Mod("Shift_L", "Shift", 4),
            K("Z"), K("X"), K("C"), K("V"), K("B"), K("N"), K("M"),
            Sym(",", "<"), Sym(".", ">"), Sym("/", "?"),
            Mod("Shift_R", "Shift", 4),
            new KeyDef("Up", "↑", null, 2, false),
        },
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
