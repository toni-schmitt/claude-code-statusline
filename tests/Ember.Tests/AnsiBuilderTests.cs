using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>§5.1's attribute discipline and §13's width accounting.</summary>
public class AnsiBuilderTests
{
    [Fact]
    public void ColoredTracksVisibleWidthNotEscapeCodes()
    {
        var b = new AnsiBuilder();
        b.Colored("hello", 230);
        Assert.Equal(5, b.Width);
    }

    [Fact]
    public void BoldIsClosedAtTheEndOfItsOwnRun()
    {
        var b = new AnsiBuilder().Colored("x", 203, bold: true).Colored("y", 245);
        var output = b.Build();

        Assert.Contains($"{Palette.BoldOn}x{Palette.BoldOff}", output);
        // The second run must not still be bold -- no second BoldOff should be needed.
        Assert.Equal(1, CountOccurrences(output, Palette.BoldOn));
    }

    [Fact]
    public void GradientAppliesOneColorPerCharacter()
    {
        var b = new AnsiBuilder();
        b.Gradient("AB", [230, 166]);
        var output = b.Build();

        Assert.Equal(2, b.Width);
        Assert.Contains($"{Palette.Fg(230)}A", output);
        Assert.Contains($"{Palette.Fg(166)}B", output);
    }

    [Fact]
    public void BarCountsOneColumnPerCell()
    {
        var cells = Meter.Render(50, null, 10);
        var b = new AnsiBuilder().Bar(cells);
        Assert.Equal(10, b.Width);
    }

    [Fact]
    public void ReverseBannerPadsOneSpaceInsideEachEnd()
    {
        var b = new AnsiBuilder().ReverseBanner("LIMIT REACHED", Palette.Critical);
        Assert.Equal("LIMIT REACHED".Length + 2, b.Width);

        var output = b.Build();
        Assert.Contains($"{Palette.ReverseOn} LIMIT REACHED {Palette.ReverseOff}", output);
    }

    [Fact]
    public void BuildAppendsExactlyOneResetAtTheEnd()
    {
        var output = new AnsiBuilder().Colored("a", 230).Colored("b", 240).Build();
        Assert.EndsWith(Palette.Reset, output);
        Assert.Equal(1, CountOccurrences(output, Palette.Reset));
    }

    [Fact]
    public void EmptyTextContributesNoWidthOrEscapes()
    {
        var b = new AnsiBuilder().Colored("", 230);
        Assert.Equal(0, b.Width);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
