using Ember.Core;
using Ember.Core.Data;
using Ember.Core.Render;
using Xunit;
using static Ember.Tests.TestSupport;

namespace Ember.Tests;

/// <summary>
/// §2's composition and §13's width degradation. Degradation tests cascade
/// off measured widths rather than hardcoded column numbers, per §13.3's own
/// instruction not to encode a table of width constants -- so these tests
/// exercise the same "compose, measure, shed" contract the implementation
/// promises, independent of exactly how wide any given string is.
/// </summary>
public class LineTests
{
    private static readonly IconGlyphs Icons = Ember.Core.Render.Icons.For(IconSet.Nerd);

    private static Line1Input MakeLine1Input(
        string? effort = "xhigh", string? branch = "main", double? ctx = 12, long? ctxTokens = null) => new(
        Icons, "Opus 5", effort, "claude-code-statusline", branch,
        TimeSpan.FromMinutes(84), ctx, ctxTokens);

    [Fact]
    public void MatchesTheSpecsOwnWorkedExampleExactly()
    {
        // §2's headline example, reproduced byte-for-byte: 70 columns.
        var line1 = Line.ComposeLine1(MakeLine1Input(), 999);
        Assert.Equal(70, VisibleWidth(line1));
        Assert.Equal(
            " Opus 5  xhigh ❯  claude-code-statusline  main ❯  1h24m ❯ ctx 12%",
            StripAnsi(line1));
    }

    [Fact]
    public void Line2MatchesTheSpecsOwnWorkedExampleExactly()
    {
        // Same example: 5h at 85% with a 22% session share, 7d at 44%, dim estimate slot. 101 columns.
        var fiveHour = new RateLimitWindow { UsedPercentage = 85, ResetsAt = 47 * 60 };
        var sevenDay = new RateLimitWindow { UsedPercentage = 44, ResetsAt = 4 * 86400 + 6 * 3600 };
        var spendData = new SpendSlotData(0.82, 2.41, "USD", null, null, Binding: null);
        var input = new Line2Input(Icons, fiveHour, sevenDay, 22, DateTimeOffset.FromUnixTimeSeconds(0), SpendSlotKind.DimEstimate, spendData);

        var line2 = Line.ComposeLine2(input, 999);

        Assert.Equal(101, VisibleWidth(line2));
        Assert.Equal(
            "5h ████████▌░ 85%  0h47m ❯ 7d ████▌░░░░░ 44%  4d6h ❯  +22% of 5h ❯  ≈$0.82 session · ≈$2.41 today",
            StripAnsi(line2));
    }

    [Fact]
    public void ProjectAndBranchNeverShedForWidth()
    {
        var input = MakeLine1Input();
        var full = Line.ComposeLine1(input, 999);
        var narrowest = Line.ComposeLine1(input, 1);

        Assert.Contains("claude-code-statusline", StripAnsi(full));
        Assert.Contains("claude-code-statusline", StripAnsi(narrowest));
        Assert.Contains("main", StripAnsi(full));
        Assert.Contains("main", StripAnsi(narrowest));
    }

    [Fact]
    public void AbsentContextHidesCtxEvenAtFullWidth()
    {
        var line1 = Line.ComposeLine1(MakeLine1Input(ctx: null), 999);
        Assert.DoesNotContain("ctx", StripAnsi(line1));
    }

    [Fact]
    public void ContextTokensRenderParentheticallyAfterThePercentage()
    {
        var line1 = StripAnsi(Line.ComposeLine1(MakeLine1Input(ctx: 12, ctxTokens: 175_100), 999));
        Assert.Contains("ctx 12% (175.1k)", line1);
    }

    [Fact]
    public void AbsentContextTokensLeaveThePercentageBare()
    {
        var line1 = StripAnsi(Line.ComposeLine1(MakeLine1Input(ctx: 12, ctxTokens: null), 999));
        Assert.Contains("ctx 12%", line1);
        Assert.DoesNotContain("(", line1);
    }

    [Fact]
    public void ContextTokensShedTogetherWithThePercentage()
    {
        var input = MakeLine1Input(ctx: 12, ctxTokens: 175_100);
        var full = StripAnsi(Line.ComposeLine1(input, 999));
        Assert.Contains("175.1k", full);

        var narrower = StripAnsi(Line.ComposeLine1(input, full.Length - 1));
        Assert.DoesNotContain("175.1k", narrower);
        Assert.DoesNotContain("ctx", narrower);
    }

    [Fact]
    public void AbsentEffortHidesEffortSegment()
    {
        var line1 = Line.ComposeLine1(MakeLine1Input(effort: null), 999);
        Assert.DoesNotContain("xhigh", StripAnsi(line1));
    }

    [Fact]
    public void AbsentBranchHidesBranchButKeepsProject()
    {
        var line1 = Line.ComposeLine1(MakeLine1Input(branch: null), 999);
        var text = StripAnsi(line1);
        Assert.Contains("claude-code-statusline", text);
        Assert.DoesNotContain("main", text);
    }

    [Fact]
    public void EmptyModelSkipsEntireModelSectionWithNoLeadingSeparator()
    {
        var input = new Line1Input(Icons, null, "high", "my-project", "main", TimeSpan.FromMinutes(5), 10);
        var line1 = Line.ComposeLine1(input, 999);
        var text = StripAnsi(line1);
        Assert.Contains("my-project", text);
        Assert.DoesNotContain("high", text); // effort is part of the model section

        var withModel = StripAnsi(Line.ComposeLine1(MakeLine1Input(), 999));
        Assert.True(text.Length < withModel.Length, "absent model should produce a shorter line");
    }

    [Fact]
    public void Line1ShedsCtxThenDurationThenEffortAsWidthShrinks()
    {
        var input = MakeLine1Input();

        var full = StripAnsi(Line.ComposeLine1(input, 999));
        Assert.Contains("ctx", full);
        Assert.Contains("1h24m", full);
        Assert.Contains("xhigh", full);
        int fullWidth = full.Length;

        var noCtx = StripAnsi(Line.ComposeLine1(input, fullWidth - 1));
        Assert.DoesNotContain("ctx", noCtx);
        Assert.Contains("1h24m", noCtx);
        Assert.Contains("xhigh", noCtx);
        int noCtxWidth = noCtx.Length;
        Assert.True(noCtxWidth < fullWidth);

        var noDuration = StripAnsi(Line.ComposeLine1(input, noCtxWidth - 1));
        Assert.DoesNotContain("ctx", noDuration);
        Assert.DoesNotContain("1h24m", noDuration);
        Assert.Contains("xhigh", noDuration);
        int noDurationWidth = noDuration.Length;
        Assert.True(noDurationWidth < noCtxWidth);

        var noEffort = StripAnsi(Line.ComposeLine1(input, noDurationWidth - 1));
        Assert.DoesNotContain("ctx", noEffort);
        Assert.DoesNotContain("1h24m", noEffort);
        Assert.DoesNotContain("xhigh", noEffort);
        Assert.Contains("claude-code-statusline", noEffort);
        Assert.Contains("main", noEffort);
    }

    private static Line2Input MakeLine2Input(bool withRateLimits = true) => new(
        Icons,
        withRateLimits ? new RateLimitWindow { UsedPercentage = 85, ResetsAt = 2820 } : null,
        withRateLimits ? new RateLimitWindow { UsedPercentage = 44, ResetsAt = 364800 } : null,
        22,
        DateTimeOffset.FromUnixTimeSeconds(0),
        SpendSlotKind.DimEstimate,
        new SpendSlotData(0.82, 2.41, "USD", null, null, Binding: null));

    private static int CountBarGlyphs(string text) => text.Count(c => c is '█' or '▌' or '░');

    [Fact]
    public void Line2ShedsInDocumentedOrderAsWidthShrinks()
    {
        var input = MakeLine2Input();

        var full = StripAnsi(Line.ComposeLine2(input, 999));
        Assert.Equal(20, CountBarGlyphs(full)); // two 10-cell bars
        Assert.Contains("session", full);
        Assert.Contains("today", full);
        Assert.Contains("+22% of 5h", full);
        int fullWidth = full.Length;

        // Tier 2: 7d countdown drops (its reset icon + value disappear; the 7d bar/pct remain).
        var noSevenDayCountdown = StripAnsi(Line.ComposeLine2(input, fullWidth - 1));
        Assert.Equal(20, CountBarGlyphs(noSevenDayCountdown));
        int t2Width = noSevenDayCountdown.Length;
        Assert.True(t2Width < fullWidth);

        // Tier 3: both bars halve from 10 to 5 cells.
        var halvedBars = StripAnsi(Line.ComposeLine2(input, t2Width - 1));
        Assert.Equal(10, CountBarGlyphs(halvedBars));
        int t3Width = halvedBars.Length;
        Assert.True(t3Width < t2Width);

        // Tier 4: the spend slot loses its "session"/"today" words but keeps the figures.
        var compactSpend = StripAnsi(Line.ComposeLine2(input, t3Width - 1));
        Assert.DoesNotContain("session", compactSpend);
        Assert.DoesNotContain("today", compactSpend);
        Assert.Contains("0.82", compactSpend);
        int t4Width = compactSpend.Length;
        Assert.True(t4Width < t3Width);

        // Tier 5: the spend slot drops entirely.
        var noSpend = StripAnsi(Line.ComposeLine2(input, t4Width - 1));
        Assert.DoesNotContain("0.82", noSpend);
        Assert.Contains("+22% of 5h", noSpend);
        int t5Width = noSpend.Length;
        Assert.True(t5Width < t4Width);

        // Tier 6: the share segment drops too -- the narrowest tier.
        var noShare = StripAnsi(Line.ComposeLine2(input, t5Width - 1));
        Assert.DoesNotContain("of 5h", noShare);
    }

    [Fact]
    public void ApiKeyProfileHasOnlyTheDimEstimate()
    {
        var input = MakeLine2Input(withRateLimits: false);
        var line2 = StripAnsi(Line.ComposeLine2(input, 999));

        Assert.Equal(0, CountBarGlyphs(line2));
        Assert.DoesNotContain("5h", line2);
        Assert.DoesNotContain("7d", line2);
        Assert.DoesNotContain("of 5h", line2);
        Assert.Contains("≈$0.82 session", line2);
        Assert.Contains("≈$2.41 today", line2);
    }

    [Fact]
    public void NeitherBarEverCarriesTheSessionShareColour()
    {
        // Both bars are coloured purely by position/severity now (§2.2); the
        // session's own share lives only in the `+N% of 5h` segment. Checked
        // on the raw (ANSI-included) output, since the ember colour is
        // exactly what a stray hot-cell override would look like --
        // stripping ANSI first would make this test unable to fail.
        var input = MakeLine2Input();
        var raw = Line.ComposeLine2(input, 999);

        int fiveHourStart = raw.IndexOf("5h ", StringComparison.Ordinal);
        string fromFiveHour = raw[fiveHourStart..];
        string fiveHourSegment = fromFiveHour[..fromFiveHour.IndexOf('❯')];
        Assert.DoesNotContain(Palette.Fg(Palette.SessionShare), fiveHourSegment);

        int sevenDayStart = raw.IndexOf("7d ", StringComparison.Ordinal);
        string fromSevenDay = raw[sevenDayStart..];
        string sevenDaySegment = fromSevenDay[..fromSevenDay.IndexOf('❯')];
        Assert.DoesNotContain(Palette.Fg(Palette.SessionShare), sevenDaySegment);
    }
}
