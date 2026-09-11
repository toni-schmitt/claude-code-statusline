namespace Ember.Core.Render;

/// <summary>One rendered bar cell: a glyph plus the colour (and bold flag) to paint it in.</summary>
public readonly record struct BarCell(char Glyph, int Color, bool Bold);

/// <summary>
/// The two-toned usage bar, §5.4. Half-divisible cells give 5% resolution;
/// the session's own contribution ("hot") renders at the leading edge of the
/// fill, ahead of the account's earlier usage ("base").
/// </summary>
public static class Meter
{
    /// <param name="usedPercentage">Window utilisation, may exceed 100.</param>
    /// <param name="sharePercentage">
    /// This session's share of the window (already clamped &gt;= 0 by the
    /// caller, §7.1), or null when there is no tail to draw (e.g. the 7-day
    /// bar, which never carries one).
    /// </param>
    /// <param name="cells">10 normally, 5 when narrow (§13).</param>
    public static BarCell[] Render(double usedPercentage, double? sharePercentage, int cells)
    {
        double perHalf = 100.0 / (2 * cells);
        int maxHalves = 2 * cells;

        int halves = usedPercentage >= 100
            ? maxHalves
            : Math.Min(maxHalves - 1, RoundHalfUp(usedPercentage / perHalf));

        int occupied = (halves + 1) / 2; // ceil(halves / 2)
        bool hasHalf = halves % 2 == 1;  // the outermost occupied cell is a half

        int hot = 0;
        if (sharePercentage is double share)
        {
            double perCell = 100.0 / cells;
            hot = Math.Min(occupied, RoundHalfUp(share / perCell));
        }
        int baseCount = occupied - hot;

        var (severityColor, severityBold) = Palette.Severity(usedPercentage);

        var result = new BarCell[cells];
        int idx = 0;

        for (int i = 0; i < baseCount; i++)
        {
            bool outermost = hot == 0 && i == baseCount - 1 && hasHalf;
            result[idx++] = new BarCell(outermost ? Half : Full, severityColor, severityBold);
        }
        for (int i = 0; i < hot; i++)
        {
            bool outermost = i == hot - 1 && hasHalf;
            result[idx++] = new BarCell(outermost ? Half : Full, Palette.SessionShare, false);
        }
        for (; idx < cells; idx++)
        {
            result[idx] = new BarCell(Empty, Palette.UnfilledBar, false);
        }

        return result;
    }

    // Placeholder glyphs; callers overwrite via RenderWithGlyphs when the icon
    // set matters (ascii has no half-block). Kept as sane defaults for tests.
    private const char Full = '█';
    private const char Half = '▌';
    private const char Empty = '░';

    /// <summary>Same as <see cref="Render"/>, but with caller-supplied bar glyphs (§5.2's per-icon-set <see cref="BarChars"/>).</summary>
    public static BarCell[] Render(double usedPercentage, double? sharePercentage, int cells, BarChars chars)
    {
        var cellsResult = Render(usedPercentage, sharePercentage, cells);
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
