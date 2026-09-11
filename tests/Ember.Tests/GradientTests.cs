using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>§5.3: every worked example from the spec, verified exactly.</summary>
public class GradientTests
{
    [Theory]
    [InlineData("Opus", new[] { 230, 216, 209, 166 })]
    [InlineData("Opus 5", new[] { 230, 223, 216, 209, 202, 166 })]
    [InlineData("Sonnet 5", new[] { 230, 223, 223, 216, 209, 202, 202, 166 })]
    [InlineData("Haiku 4.5", new[] { 230, 223, 223, 216, 209, 209, 202, 202, 166 })]
    [InlineData("Claude Opus 5", new[] { 230, 230, 223, 223, 216, 216, 209, 209, 209, 202, 202, 166, 166 })]
    public void MatchesWorkedExamples(string text, int[] expected)
    {
        Assert.Equal(expected, Gradient.ForText(text));
    }

    [Fact]
    public void SingleCharacterIsFirstStop()
    {
        Assert.Equal([230], Gradient.ForText("X"));
    }

    [Fact]
    public void EmptyStringYieldsNoColors()
    {
        Assert.Empty(Gradient.ForText(""));
    }

    [Fact]
    public void FirstCharacterAlwaysFirstStopAndLastAlwaysLastStop()
    {
        // Excludes single-character names: §5.3 special-cases len==1 to a single 230, not 230-then-166.
        foreach (var name in new[] { "AB", "Sonnet", "A Very Long Model Name Indeed" })
        {
            var colors = Gradient.ForText(name);
            Assert.Equal(230, colors[0]);
            Assert.Equal(166, colors[^1]);
        }
    }
}
