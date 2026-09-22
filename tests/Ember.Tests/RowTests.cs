using System.Text.Json;
using Ember.Core.Data;
using Ember.Core.Render;
using Ember.Subagent;
using Xunit;
using static Ember.Tests.TestSupport;

namespace Ember.Tests;

/// <summary>§11.3's row composition.</summary>
public class RowTests
{
    private static readonly IconGlyphs Icons = Ember.Core.Render.Icons.For(IconSet.Nerd);
    private static readonly IconGlyphs AsciiIcons = Ember.Core.Render.Icons.For(IconSet.Ascii);

    private static SubagentTask Task(
        string? name = "Explore", string? type = null, string status = "running", string? model = "claude-haiku-4-5-20251001",
        string? effortJson = null, long? contextWindowSize = 200_000, long? tokenCount = 36_000,
        string? description = "locate the render call sites") => JsonSerializer.Deserialize<SubagentTask>(
        $$"""
        {
            "name": {{Json(name)}},
            "type": {{Json(type)}},
            "status": {{Json(status)}},
            "model": {{Json(model)}},
            "effort": {{effortJson ?? "null"}},
            "contextWindowSize": {{(contextWindowSize?.ToString() ?? "null")}},
            "tokenCount": {{(tokenCount?.ToString() ?? "null")}},
            "description": {{Json(description)}}
        }
        """, SubagentJsonContext.Default.SubagentTask)!;

    /// <summary>A JSON string literal, or <c>null</c>, so a fixture value with quotes or backslashes still deserialises.</summary>
    private static string Json(string? value) => value is null ? "null" : JsonSerializer.Serialize(value);

    [Fact]
    public void RunningRowUsesFullGradientAndSeverityBar()
    {
        var content = Row.Compose(Task(status: "running"), Icons, IconSet.Nerd, 100);
        var text = StripAnsi(content);

        Assert.Contains("Explore", text);
        Assert.Contains("Haiku 4.5", text);
        Assert.Contains("18%", text); // 36000/200000
        Assert.Contains("36.0k", text);
        Assert.Contains(Palette.Fg(Palette.Branch), content); // running marker colour present somewhere
    }

    [Fact]
    public void FailedRowKeepsFullGradientButRedMarker()
    {
        var content = Row.Compose(Task(status: "failed", name: "risky-task"), Icons, IconSet.Nerd, 100);
        Assert.Contains(Palette.Fg(Palette.Critical), content);
        // The name itself still gets the gradient treatment (§11.3: "full palette" for failed).
        Assert.Contains(Palette.Fg(Gradient.ForText("risky-task")[0]), content);
    }

    [Fact]
    public void QueuedRowWithNonzeroUsageRendersEntirelyFlat()
    {
        // Regression test: an earlier version only flattened the name, leaving
        // separators and the bar's severity colour at full brightness.
        var content = Row.Compose(
            Task(status: "queued", name: "code-review", model: "claude-opus-5-20260101",
                effortJson: "\"xhigh\"", contextWindowSize: 200_000, tokenCount: 140_000),
            Icons, IconSet.Nerd, 100);

        var text = StripAnsi(content);
        Assert.Contains("70%", text); // 140000/200000 -- bar shape is preserved
        Assert.Contains("code-review", text);

        // Every colour code in the whole row must be either 240 (flat) or the reset.
        foreach (var colorCode in ExtractColorCodes(content))
        {
            Assert.Equal(Palette.UnfilledBar, colorCode);
        }
    }

    [Fact]
    public void DoneRowRendersEntirelyFlat()
    {
        var content = Row.Compose(Task(status: "done", name: "finished-task", model: null, tokenCount: null, contextWindowSize: null), Icons, IconSet.Nerd, 100);
        foreach (var colorCode in ExtractColorCodes(content))
        {
            Assert.Equal(Palette.UnfilledBar, colorCode);
        }
    }

    [Fact]
    public void QueuedMarkerAndDoneMarkerDiffer()
    {
        var queued = StripAnsi(Row.Compose(Task(status: "queued"), Icons, IconSet.Nerd, 100));
        var done = StripAnsi(Row.Compose(Task(status: "done"), Icons, IconSet.Nerd, 100));

        Assert.NotEqual(queued[0], done[0]);
        Assert.StartsWith(Icons.Done, done); // §5.2's "done" nerd glyph
        Assert.False(done.StartsWith(queued[0])); // queued's marker never leaks into a done row
    }

    [Fact]
    public void AsciiIconSetUsesAsciiMarkers()
    {
        var running = StripAnsi(Row.Compose(Task(status: "running"), AsciiIcons, IconSet.Ascii, 100));
        var queued = StripAnsi(Row.Compose(Task(status: "queued"), AsciiIcons, IconSet.Ascii, 100));
        Assert.StartsWith("*", running);
        Assert.StartsWith(".", queued);
    }

    [Fact]
    public void NumericEffortRendersAsCompactTokenBudget()
    {
        var content = Row.Compose(Task(effortJson: "50000"), Icons, IconSet.Nerd, 100);
        Assert.Contains("50.0k", StripAnsi(content));
    }

    [Fact]
    public void StringEffortRendersVerbatim()
    {
        var content = Row.Compose(Task(effortJson: "\"high\""), Icons, IconSet.Nerd, 100);
        Assert.Contains("high", StripAnsi(content));
    }

    [Fact]
    public void MissingEffortRendersNothingExtra()
    {
        var withEffort = StripAnsi(Row.Compose(Task(effortJson: "\"high\""), Icons, IconSet.Nerd, 100));
        var withoutEffort = StripAnsi(Row.Compose(Task(effortJson: null), Icons, IconSet.Nerd, 100));
        Assert.Contains("high", withEffort);
        Assert.DoesNotContain("high", withoutEffort);
    }

    [Fact]
    public void DescriptionIsTruncatedToFitAvailableColumns()
    {
        var longDescription = new string('x', 500);
        var content = Row.Compose(Task(description: longDescription), Icons, IconSet.Nerd, 60);
        Assert.True(VisibleWidth(content) <= 60);
    }

    [Fact]
    public void MissingModelSkipsTheModelSegmentEntirely()
    {
        var content = StripAnsi(Row.Compose(Task(model: null), Icons, IconSet.Nerd, 100));
        Assert.DoesNotContain("Haiku", content);
    }

    [Fact]
    public void ZeroContextRendersEmptyBarAndZeroPercent()
    {
        var content = StripAnsi(Row.Compose(Task(contextWindowSize: null, tokenCount: null), Icons, IconSet.Nerd, 100));
        Assert.Contains("0%", content);
        Assert.Contains("░░░░░░░░░░", content);
    }

    [Fact]
    public void DescriptionWithSurrogatePairsDoesNotSplitThem()
    {
        var desc = "emoji: \U0001F600\U0001F601\U0001F602 done";
        // Once in the trailing slot (named row) and once promoted through the gradient (unnamed, active row).
        AssertSurrogatePairsIntact(Row.Compose(Task(description: desc), Icons, IconSet.Nerd, 60));
        AssertSurrogatePairsIntact(Row.Compose(Task(name: null, description: desc), Icons, IconSet.Nerd, 100));
    }

    /// <summary>Inspects the raw ANSI content: an escape between the two halves of a pair is the defect, and stripping escapes first would hide it.</summary>
    private static void AssertSurrogatePairsIntact(string content)
    {
        for (int i = 0; i < content.Length; i++)
        {
            if (char.IsHighSurrogate(content[i]))
                Assert.True(i + 1 < content.Length && char.IsLowSurrogate(content[i + 1]), "Surrogate pair was split");
        }
    }

    [Fact]
    public void NamedRowKeepsTheDescriptionAtTheEnd()
    {
        var text = StripAnsi(Row.Compose(Task(name: "Explore", description: "locate the render call sites"), Icons, IconSet.Nerd, 100));
        Assert.StartsWith("● Explore", text);
        Assert.EndsWith("locate the render call sites", text);
    }

    [Fact]
    public void UnnamedRowPromotesTheDescriptionInsteadOfShowingTheType()
    {
        // Every plain Agent-tool spawn arrives as type "local_agent" with no name;
        // the description is the stable text that tells those rows apart.
        var text = StripAnsi(Row.Compose(
            Task(name: null, type: "local_agent", description: "Probe model-opus-4-8"), Icons, IconSet.Nerd, 100));
        Assert.StartsWith("● Probe model-opus-4-8", text);
        Assert.DoesNotContain("local_agent", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "Probe model-opus-4-8"));
    }

    [Fact]
    public void UnnamedRowWithoutDescriptionFallsBackToTheType()
    {
        var text = StripAnsi(Row.Compose(Task(name: null, type: "local_agent", description: null), Icons, IconSet.Nerd, 100));
        Assert.StartsWith("● local_agent", text);
    }

    [Fact]
    public void UnnamedRowWithLongDescriptionStillFitsTheColumns()
    {
        var longDescription = new string('x', 500);
        var content = Row.Compose(Task(name: null, description: longDescription), Icons, IconSet.Nerd, 60);
        var text = StripAnsi(content);
        Assert.True(VisibleWidth(content) <= 60);
        Assert.Contains("ctx", text); // the metrics survive; only the promoted description is cut
        Assert.Contains(new string('x', 10), text); // and cut, not removed
    }

    [Fact]
    public void CompletedRowUsesTheDoneMarkerAndRendersFlat()
    {
        // Claude Code reports a finished task as "completed"; §11.1's "done" is accepted alongside it.
        var content = Row.Compose(Task(status: "completed"), Icons, IconSet.Nerd, 100);
        Assert.StartsWith(Icons.Done, StripAnsi(content));
        foreach (var colorCode in ExtractColorCodes(content))
        {
            Assert.Equal(Palette.UnfilledBar, colorCode);
        }
    }

    [Fact]
    public void NamedRowWithoutDescriptionEndsAfterTheMetrics()
    {
        var text = StripAnsi(Row.Compose(Task(name: "probe", description: null), Icons, IconSet.Nerd, 100));
        Assert.EndsWith("36.0k", text); // no separator pointing at nothing
    }

    [Fact]
    public void NamedRowDropsTheSeparatorWhenNoRoomIsLeftForTheDescription()
    {
        // Marker, "Explore" and the metrics take 48 columns; a 3-column separator with nothing after it must not follow.
        var content = Row.Compose(Task(), Icons, IconSet.Nerd, 50);
        Assert.EndsWith("36.0k", StripAnsi(content));
        Assert.True(VisibleWidth(content) <= 50);
    }

    [Fact]
    public void UnnamedRowWithEmptyDescriptionFallsBackToTheType()
    {
        var text = StripAnsi(Row.Compose(Task(name: null, type: "local_agent", description: ""), Icons, IconSet.Nerd, 100));
        Assert.StartsWith("● local_agent", text);
    }

    [Fact]
    public void NamedRowWithLongNameStillFitsTheColumns()
    {
        var content = Row.Compose(Task(name: new string('n', 200)), Icons, IconSet.Nerd, 60);
        Assert.True(VisibleWidth(content) <= 60);
        Assert.Contains("ctx", StripAnsi(content)); // the metrics survive; the name is what gives way
    }

    private static IEnumerable<int> ExtractColorCodes(string ansiText)
    {
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(ansiText, @"\x1b\[38;5;(\d+)m"))
        {
            yield return int.Parse(m.Groups[1].Value);
        }
    }
}
