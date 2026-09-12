using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>§5.4: every worked example from the spec's bar table, cell by cell.</summary>
public class MeterTests
{
    private static string Glyphs(BarCell[] cells) => new(cells.Select(c => c.Glyph).ToArray());

    [Fact]
    public void FortyFourPercentEntirelyCalm()
    {
        var cells = Meter.Render(44, 10);
        Assert.Equal("████▌░░░░░", Glyphs(cells));
        // every occupied cell's own position is still under the 60% notice threshold.
        for (int i = 0; i < 5; i++) Assert.Equal((Palette.GradientStop2, false), (cells[i].Color, cells[i].Bold));
        for (int i = 5; i < 10; i++) Assert.Equal((Palette.UnfilledBar, false), (cells[i].Color, cells[i].Bold));
    }

    [Fact]
    public void EightyFivePercentGradientAcrossBands()
    {
        var cells = Meter.Render(85, 10);
        Assert.Equal("████████▌░", Glyphs(cells));
        // positional gradient: calm -> notice -> warn as position climbs toward 85%, not one flat colour.
        for (int i = 0; i < 5; i++) Assert.Equal((Palette.GradientStop2, false), (cells[i].Color, cells[i].Bold));
        Assert.Equal((Palette.Notice, false), (cells[5].Color, cells[5].Bold));
        Assert.Equal((Palette.Notice, false), (cells[6].Color, cells[6].Bold));
        Assert.Equal((Palette.Warn, false), (cells[7].Color, cells[7].Bold));
        Assert.Equal((Palette.Warn, false), (cells[8].Color, cells[8].Bold));
        Assert.Equal('▌', cells[8].Glyph);
        Assert.Equal((Palette.UnfilledBar, false), (cells[9].Color, cells[9].Bold));
    }

    [Fact]
    public void NinetyFivePercentEndsCriticalAndBoldAtTheTip()
    {
        var cells = Meter.Render(95, 10);
        Assert.Equal("█████████▌", Glyphs(cells));
        // only the cell whose own range reaches the critical band (>= 95) is critical/bold; earlier cells still read the gradient.
        for (int i = 0; i < 5; i++) Assert.Equal((Palette.GradientStop2, false), (cells[i].Color, cells[i].Bold));
        Assert.Equal((Palette.Notice, false), (cells[5].Color, cells[5].Bold));
        Assert.Equal((Palette.Notice, false), (cells[6].Color, cells[6].Bold));
        Assert.Equal((Palette.Warn, false), (cells[7].Color, cells[7].Bold));
        Assert.Equal((Palette.Warn, false), (cells[8].Color, cells[8].Bold));
        Assert.Equal((Palette.Critical, true), (cells[9].Color, cells[9].Bold));
    }

    [Fact]
    public void NinetyNinePercentLooksLikeNinetyFive()
    {
        // The min(2*cells-1, ...) clamp reserves the last half-cell for 100% exactly (§5.4).
        var cells = Meter.Render(99, 10);
        Assert.Equal("█████████▌", Glyphs(cells));
    }

    [Fact]
    public void OneHundredPercentIsFullWithNoHalf()
    {
        var cells = Meter.Render(100, 10);
        Assert.Equal("██████████", Glyphs(cells));
    }

    [Fact]
    public void AboveOneHundredClampsFullAndStaysCriticalAtTheTip()
    {
        var cells = Meter.Render(112, 10);
        Assert.Equal("██████████", Glyphs(cells));
        Assert.Equal((Palette.Critical, true), (cells[9].Color, cells[9].Bold));
    }

    [Fact]
    public void FivePercentIsALoneCalmHalfCell()
    {
        var cells = Meter.Render(5, 10);
        Assert.Equal("▌░░░░░░░░░", Glyphs(cells));
        Assert.Equal((Palette.GradientStop2, false), (cells[0].Color, cells[0].Bold));
    }

    [Fact]
    public void FiveCellsHalvesResolutionForNarrowTerminals()
    {
        var cells = Meter.Render(50, 5);
        Assert.Equal(5, cells.Length);
        Assert.Equal("██▌░░", Glyphs(cells));
        // 5 cells -> 20 points each; the outermost (3rd) cell's range [40,60) crosses into notice.
        Assert.Equal((Palette.GradientStop2, false), (cells[0].Color, cells[0].Bold));
        Assert.Equal((Palette.GradientStop2, false), (cells[1].Color, cells[1].Bold));
        Assert.Equal((Palette.Notice, false), (cells[2].Color, cells[2].Bold));
    }

    [Fact]
    public void BarCharsOverloadRemapsGlyphsPerIconSet()
    {
        var ascii = Icons.For(IconSet.Ascii).Bar;
        var cells = Meter.Render(44, 10, ascii);
        Assert.Equal("####+-----", Glyphs(cells));
    }
}
