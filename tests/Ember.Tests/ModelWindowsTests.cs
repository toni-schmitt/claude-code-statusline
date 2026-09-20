using System.Text.Json;
using Ember.Core;
using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

/// <summary>
/// §5.7: projecting §4.1's <c>limits[]</c> into the per-model weekly windows
/// line 2 renders. Parsing is exercised through the real JSON contract rather
/// than hand-built objects, because the wire spelling of <c>resets_at</c> is
/// half of what this projection has to survive.
/// </summary>
public class ModelWindowsTests
{
    private static UsageResponse Parse(string json) =>
        JsonSerializer.Deserialize(json, UsageJsonContext.Default.UsageResponse)!;

    private const string FableRow = """
        { "kind": "weekly_scoped", "percent": 37, "resets_at": "2026-09-23T04:00:00+00:00",
          "scope": { "model": { "display_name": "Fable", "id": null } } }
        """;

    [Fact]
    public void ColdCacheYieldsNothing()
    {
        Assert.Empty(ModelWindows.From(null));
        Assert.Empty(ModelWindows.From(Parse("""{ "five_hour": { "utilization": 3 } }""")));
        Assert.Empty(ModelWindows.From(Parse("""{ "limits": [] }""")));
    }

    [Fact]
    public void ReadsTheModelBucketAsALowerCasedLabel()
    {
        var windows = ModelWindows.From(Parse($$"""{ "limits": [ {{FableRow}} ] }"""));

        var fable = Assert.Single(windows);
        Assert.Equal("fable", fable.Label);
        Assert.Equal(37, fable.UsedPercentage);
        Assert.Equal(DateTimeOffset.Parse("2026-09-23T04:00:00+00:00"), fable.ResetsAt);
    }

    [Fact]
    public void IgnoresEveryRowThatIsNotScopedToAModel()
    {
        // session and weekly_all repeat five_hour and seven_day, which stdin
        // already delivers; a weekly_scoped row with no display_name has
        // nothing to label a bar with.
        var windows = ModelWindows.From(Parse($$"""
            { "limits": [
                { "kind": "session", "percent": 90, "resets_at": null, "scope": null },
                { "kind": "weekly_all", "percent": 80, "resets_at": null, "scope": null },
                { "kind": "weekly_scoped", "percent": 50, "resets_at": null, "scope": { "model": { "display_name": null } } },
                { "kind": "weekly_scoped", "percent": 50, "resets_at": null, "scope": null },
                {{FableRow}}
            ] }
            """));

        Assert.Equal(new[] { "fable" }, windows.Select(w => w.Label));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.49, false)]
    [InlineData(0.5, true)]
    [InlineData(100, true)]
    public void HiddenUntilTheLabelWouldReadAboveZero(double percent, bool expected)
    {
        var windows = ModelWindows.From(Parse($$"""
            { "limits": [ { "kind": "weekly_scoped", "percent": {{percent}}, "resets_at": null,
                            "scope": { "model": { "display_name": "Fable" } } } ] }
            """));

        Assert.Equal(expected, windows.Count is 1);
    }

    [Fact]
    public void KeepsPayloadOrderAcrossSeveralBuckets()
    {
        var windows = ModelWindows.From(Parse("""
            { "limits": [
                { "kind": "weekly_scoped", "percent": 12, "resets_at": null, "scope": { "model": { "display_name": "Fable" } } },
                { "kind": "weekly_scoped", "percent": 44, "resets_at": null, "scope": { "model": { "display_name": "Opus" } } }
            ] }
            """));

        Assert.Equal(new[] { "fable", "opus" }, windows.Select(w => w.Label));
    }

    [Fact]
    public void AcceptsEitherSpellingOfResetsAt()
    {
        // The endpoint sends ISO 8601, but Claude Code's own reader branches on
        // a numeric epoch, so both reach us. A throw here would cost the whole
        // response, credits included (§10 rule 5).
        var windows = ModelWindows.From(Parse("""
            { "limits": [
                { "kind": "weekly_scoped", "percent": 10, "resets_at": 1790136000, "scope": { "model": { "display_name": "Fable" } } },
                { "kind": "weekly_scoped", "percent": 10, "resets_at": null, "scope": { "model": { "display_name": "Opus" } } },
                { "kind": "weekly_scoped", "percent": 10, "resets_at": "not a date", "scope": { "model": { "display_name": "Sonnet" } } }
            ] }
            """));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790136000), windows[0].ResetsAt);
        Assert.Null(windows[1].ResetsAt);
        Assert.Null(windows[2].ResetsAt);
    }

    [Fact]
    public void ReadsARowAsTheEndpointActuallySendsIt()
    {
        // The live shape, unabridged: fields this projection has no use for
        // (group, severity, is_active, scope.surface) must be ignored rather
        // than rejected, because rejecting one costs the whole response and
        // with it the credit figures (§10 rule 5).
        var usage = Parse("""
            { "limits": [ { "group": "weekly", "is_active": false, "kind": "weekly_scoped",
                            "percent": 37, "resets_at": "2026-09-23T04:00:00.003469+00:00",
                            "scope": { "model": { "display_name": "Fable", "id": null }, "surface": null },
                            "severity": "normal" } ],
              "extra_usage": { "is_enabled": true, "used_credits": 1840.0, "currency": "EUR" } }
            """);

        var fable = Assert.Single(ModelWindows.From(usage));
        Assert.Equal("fable", fable.Label);
        Assert.Equal(1840, usage.ExtraUsage?.UsedCredits);
    }
}
