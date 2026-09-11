namespace Ember.Core.Render;

public enum IconSet
{
    Nerd,
    Unicode,
    Ascii,
}

/// <summary>Bar cell glyphs, half-divisible per §5.4.</summary>
public readonly record struct BarChars(char Full, char Half, char Empty);

/// <summary>One resolved icon glyph set, §5.2.</summary>
public sealed record IconGlyphs(
    string Model,
    string Effort,
    string Project,
    string Branch,
    string Clock,
    string Share,
    string Spend,
    string Credits,
    string Blocked,
    string Reset,
    string Done,
    string Separator,
    BarChars Bar);

public static class Icons
{
    // Every glyph below is written as a \u escape rather than a literal
    // character. The Nerd Font row is Private Use Area codepoints that are
    // invisible or tofu outside the required font (§5.2) -- writing them
    // literally in source is exactly the trap that table warns about.
    public static IconGlyphs For(IconSet set) => set switch
    {
        IconSet.Nerd => new IconGlyphs(
            Model: "",    // microchip
            Effort: "",   // bolt
            Project: "",  // folder
            Branch: "",   // git branch
            Clock: "",    // clock
            Share: "",    // line chart
            Spend: "",    // dollar
            Credits: "",  // money
            Blocked: "",  // ban
            Reset: "",    // refresh
            Done: "",     // check
            Separator: "❯", // not in §5.2's icon table; Menlo and JetBrainsMono both carry it
            Bar: new BarChars(Full: '█', Half: '▌', Empty: '░')),

        IconSet.Unicode => new IconGlyphs(
            Model: "◆",    // diamond
            Effort: "▲",   // triangle
            Project: "▸",  // small triangle
            Branch: "┣",   // heavy vertical/right
            Clock: "◷",    // white circle with upper right quadrant
            Share: "⊕",    // circled plus
            Spend: "¤",    // currency sign
            Credits: "⚡",  // high voltage
            Blocked: "⊘",  // circled division slash
            Reset: "↺",    // anticlockwise open circle arrow
            Done: "✔",     // heavy check mark
            Separator: "❯",
            Bar: new BarChars(Full: '█', Half: '▌', Empty: '░')),

        IconSet.Ascii => new IconGlyphs(
            Model: "*",
            Effort: "^",
            Project: ">",
            Branch: "#",
            Clock: "@",
            Share: "+",
            Spend: "$",
            Credits: "!",
            Blocked: "X",
            Reset: "~",
            Done: "x",
            Separator: ">", // no ASCII equivalent specified for the separator; kept single-width and consistent with the other markers
            Bar: new BarChars('#', '+', '-')), // no half-block in ASCII; '+' stands in for the half cell

        _ => throw new ArgumentOutOfRangeException(nameof(set)),
    };
}
