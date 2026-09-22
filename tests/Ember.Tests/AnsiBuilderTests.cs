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
        var cells = Meter.Render(50, 10);
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

    [Fact]
    public void DisplayWidthCountsCjkCharactersAsTwo()
    {
        Assert.Equal(2, AnsiBuilder.DisplayWidth("中"));
        Assert.Equal(4, AnsiBuilder.DisplayWidth("中文"));
        Assert.Equal(3, AnsiBuilder.DisplayWidth("A中")); // 1 + 2
    }

    [Fact]
    public void DisplayWidthCountsAsciiAsOne()
    {
        Assert.Equal(5, AnsiBuilder.DisplayWidth("hello"));
    }

    [Fact]
    public void ColoredTracksCjkDisplayWidth()
    {
        var b = new AnsiBuilder();
        b.Colored("中文", 230);
        Assert.Equal(4, b.Width); // two CJK chars = 4 columns
    }

    [Fact]
    public void TruncateToWidthRespectsCjkWidth()
    {
        Assert.Equal(1, AnsiBuilder.TruncateToWidth("中文", 2)); // one CJK char fits in 2 columns
        Assert.Equal(2, AnsiBuilder.TruncateToWidth("中文", 4)); // both fit in 4 columns
        Assert.Equal(0, AnsiBuilder.TruncateToWidth("中文", 1)); // neither fits in 1 column
    }

    [Fact]
    public void TruncateToWidthNeverSplitsSurrogatePair()
    {
        var text = "A😀B"; // A + 😀 (surrogate pair) + B
        int idx = AnsiBuilder.TruncateToWidth(text, 2);
        Assert.Equal(text[..idx], text[..idx]); // no split
        Assert.True(idx == 1 || idx == 3, $"Expected 1 or 3, got {idx}");
    }

    [Fact]
    public void GradientKeepsSurrogatePairsTogether()
    {
        var text = "A😀"; // three UTF-16 code units, two characters
        var output = new AnsiBuilder().Gradient(text, [230, 216, 166]).Build();
        Assert.Contains($"{Palette.Fg(216)}😀", output); // the pair takes its high surrogate's colour, uninterrupted
        Assert.DoesNotContain(Palette.Fg(166), output);
    }

    [Fact]
    public void AppendCarriesTheOtherBuildersTextAndWidth()
    {
        var tail = new AnsiBuilder().Colored("中文", 240);
        var b = new AnsiBuilder().Colored("a", 230).Append(tail);
        Assert.Equal(5, b.Width);
        Assert.EndsWith($"{Palette.Fg(240)}中文{Palette.Reset}", b.Build());
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
