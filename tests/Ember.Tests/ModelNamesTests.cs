using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

/// <summary>Model-ID -&gt; display-name resolution for subagent rows.</summary>
public class ModelNamesTests
{
    [Theory]
    [InlineData("claude-opus-5", "Opus 5")]
    [InlineData("claude-opus-4-8", "Opus 4.8")]
    [InlineData("claude-sonnet-4-6", "Sonnet 4.6")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [InlineData("claude-fable-5-1", "Fable 5.1")]
    [InlineData("claude-fable-5", "Fable 5")]
    public void ResolvesEveryFamilyClaudeCodeHandsOver(string id, string expected)
    {
        Assert.Equal(expected, ModelNames.Resolve(id));
    }

    [Theory]
    [InlineData("claude-opus-4-8[1m]", "Opus 4.8")]
    [InlineData("claude-opus-5[1m]", "Opus 5")]
    [InlineData("claude-fable-5-1[1m]", "Fable 5.1")]
    [InlineData("claude-sonnet-4-6-20260101[1m]", "Sonnet 4.6")]
    public void DropsTheContextWindowTagBeforeParsingTheVersion(string id, string expected)
    {
        // Left in, "8[1m]" fails the digit check and a 1M-window Opus 4.8 reads "Opus 4".
        Assert.Equal(expected, ModelNames.Resolve(id));
    }

    [Theory]
    [InlineData("gpt-5")]
    [InlineData("some-custom-model")]
    [InlineData("")]
    public void UnrecognisedShapeFallsBackToTheRawId(string id)
    {
        Assert.Equal(id, ModelNames.Resolve(id));
    }

    [Fact]
    public void UnrecognisedShapeStillDropsTheContextWindowTag()
    {
        Assert.Equal("claude-neptune-6", ModelNames.Resolve("claude-neptune-6[1m]"));
        Assert.Equal("[1m]", ModelNames.Resolve("[1m]")); // nothing left to show once the tag is cut, so the tag stays
    }
}
