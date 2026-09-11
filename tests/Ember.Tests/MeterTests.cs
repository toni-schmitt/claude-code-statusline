using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>§5.4: every worked example from the spec's bar table, cell by cell.</summary>
public class MeterTests
{
    private static string Glyphs(BarCell[] cells) => new(cells.Select(c => c.Glyph).ToArray());

    [Fact]
    public void FortyFourPercentNoShare()
    {
        var cells = Meter.Render(44, null, 10);
        Assert.Equal("████▌░░░░░", Glyphs(cells));
        // base cells (normal severity, §5.5) carry the fill; the lone half is the outermost occupied cell.
        for (int i = 0; i < 4; i++) Assert.Equal((Palette.GradientStop2, false), (cells[i].Color, cells[i].Bold));
        Assert.Equal((Palette.GradientStop2, false), (cells[4].Color, cells[4].Bold));
        for (int i = 5; i < 10; i++) Assert.Equal((Palette.UnfilledBar, false), (cells[i].Color, cells[i].Bold));
    }

    [Fact]
    public void EightyFivePercentShareTwentyTwo()
    {
        var cells = Meter.Render(85, 22, 10);
        Assert.Equal("████████▌░", Glyphs(cells));
        // 7 base (warn severity, §5.5) then 2 hot (ember 202); the outermost occupied cell -- the last hot one -- is the half.
        for (int i = 0; i < 7; i++) Assert.Equal((Palette.Warn, false), (cells[i].Color, cells[i].Bold));
        Assert.Equal((Palette.SessionShare, false), (cells[7].Color, cells[7].Bold));
        Assert.Equal((Palette.SessionShare, false), (cells[8].Color, cells[8].Bold));
        Assert.Equal('▌', cells[8].Glyph);
        Assert.Equal((Palette.UnfilledBar, false), (cells[9].Color, cells[9].Bold));
    }

    [Fact]
    public void NinetyFivePercentIsCriticalAndBold()
    {
        var cells = Meter.Render(95, null, 10);
        Assert.Equal("█████████▌", Glyphs(cells));
        for (int i = 0; i < 10; i++) Assert.Equal((Palette.Critical, true), (cells[i].Color, cells[i].Bold));
    }

    [Fact]
    public void NinetyNinePercentLooksLikeNinetyFive()
    {
        // The min(2*cells-1, ...) clamp reserves the last half-cell for 100% exactly (§5.4).
        var cells = Meter.Render(99, null, 10);
        Assert.Equal("█████████▌", Glyphs(cells));
    }

    [Fact]
    public void OneHundredPercentIsFullWithNoHalf()
    {
        var cells = Meter.Render(100, null, 10);
        Assert.Equal("██████████", Glyphs(cells));
    }

    [Fact]
    public void AboveOneHundredClampsFullButKeepsShareTail()
    {
        var cells = Meter.Render(112, 5, 10);
        Assert.Equal("██████████", Glyphs(cells));
        // 9 base (critical) + 1 hot (ember) -- indistinguishable by glyph, distinct by colour.
        for (int i = 0; i < 9; i++) Assert.Equal((Palette.Critical, true), (cells[i].Color, cells[i].Bold));
        Assert.Equal((Palette.SessionShare, false), (cells[9].Color, cells[9].Bold));
    }

    [Fact]
    public void FivePercentIsALoneHalfCell()
    {
        var cells = Meter.Render(5, null, 10);
        Assert.Equal("▌░░░░░░░░░", Glyphs(cells));
        Assert.Equal((Palette.GradientStop2, false), (cells[0].Color, cells[0].Bold));
    }

    [Fact]
    public void ZeroShareRendersNoHotTail()
    {
        var cells = Meter.Render(50, 0, 10);
        // share == 0 clamps out at the caller (§7.1's max(0, ...)), but Meter itself must also treat it as "no tail".
        Assert.All(cells.Take(5), c => Assert.Equal(Palette.Severity(50).Color, c.Color));
    }

    [Fact]
    public void FiveCellsHalvesResolutionForNarrowTerminals()
    {
        var cells = Meter.Render(50, null, 5);
        Assert.Equal(5, cells.Length);
        Assert.Equal("██▌░░", Glyphs(cells));
    }

    [Fact]
    public void BarCharsOverloadRemapsGlyphsPerIconSet()
    {
        var ascii = Icons.For(IconSet.Ascii).Bar;
        var cells = Meter.Render(44, null, 10, ascii);
        Assert.Equal("####+-----", Glyphs(cells));
    }
}
