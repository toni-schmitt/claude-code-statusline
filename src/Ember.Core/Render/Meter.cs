namespace Ember.Core.Render;

/// <summary>One rendered bar cell: a glyph plus the colour (and bold flag) to paint it in.</summary>
public readonly record struct BarCell(char Glyph, int Color, bool Bold);

/// <summary>
/// The usage bar, §5.4. Half-divisible cells give 5% resolution. Colour is
/// positional, not session-derived: cell <c>i</c> of <c>cells</c> covers the
/// percentage range <c>[i, i+1) * 100/cells</c>, and is coloured by
/// §5.5's severity bands for *that* range -- so the bar always reads calm
/// to critical left to right as it fills, independent of which session (if
/// any) is running. This session's own share of the window never touches
/// the bar; it has its own segment (`+N% of 5h`).
/// </summary>
public static class Meter
{
    /// <param name="usedPercentage">Window utilisation, may exceed 100.</param>
    /// <param name="cells">10 normally, 5 when narrow (§13).</param>
    public static BarCell[] Render(double usedPercentage, int cells)
    {
        double perHalf = 100.0 / (2 * cells);
        int maxHalves = 2 * cells;

        int halves = usedPercentage >= 100
            ? maxHalves
            : Math.Min(maxHalves - 1, RoundHalfUp(usedPercentage / perHalf));

        int occupied = (halves + 1) / 2; // ceil(halves / 2)
        bool hasHalf = halves % 2 == 1;  // the outermost occupied cell is a half

        double perCell = 100.0 / cells;
        var result = new BarCell[cells];

        for (int i = 0; i < occupied; i++)
        {
            bool outermost = i == occupied - 1 && hasHalf;
            var (color, bold) = Palette.Severity((i + 1) * perCell);
            result[i] = new BarCell(outermost ? Half : Full, color, bold);
        }
        for (int i = occupied; i < cells; i++)
        {
            result[i] = new BarCell(Empty, Palette.UnfilledBar, false);
        }

        return result;
    }

    // Placeholder glyphs; callers overwrite via RenderWithGlyphs when the icon
    // set matters (ascii has no half-block). Kept as sane defaults for tests.
    private const char Full = '█';
    private const char Half = '▌';
    private const char Empty = '░';

    /// <summary>Same as <see cref="Render"/>, but with caller-supplied bar glyphs (§5.2's per-icon-set <see cref="BarChars"/>).</summary>
    public static BarCell[] Render(double usedPercentage, int cells, BarChars chars)
    {
        var cellsResult = Render(usedPercentage, cells);
        for (int i = 0; i < cellsResult.Length; i++)
        {
            var c = cellsResult[i];
            char glyph = c.Glyph switch
            {
                Full => chars.Full,
                Half => chars.Half,
                Empty => chars.Empty,
                _ => c.Glyph,
            };
            cellsResult[i] = c with { Glyph = glyph };
        }
        return cellsResult;
    }

    private static int RoundHalfUp(double x) => (int)Math.Floor(x + 0.5 + 1e-9);
}
