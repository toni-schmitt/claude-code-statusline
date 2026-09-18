using Ember.Core;
using Ember.Core.Data;
using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>§6's state machine and its five render tenants.</summary>
public class SpendSlotTests
{
    private static readonly IconGlyphs Icons = Ember.Core.Render.Icons.For(IconSet.Nerd);
    private static readonly IconGlyphs AsciiIcons = Ember.Core.Render.Icons.For(IconSet.Ascii);

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    private static UsageResponse Usage(bool? isEnabled, long? usedCredits = null) => new()
    {
        ExtraUsage = new ExtraUsage { IsEnabled = isEnabled, UsedCredits = usedCredits },
    };

    /// <summary>A stdin rate-limits object; each window is omitted when its percentage is null, and resets the given number of seconds from <see cref="Now"/>.</summary>
    private static RateLimitsInfo Limits(
        double? five = null, double? seven = null,
        long fiveResetsIn = 3600, long sevenResetsIn = 86_400) => new()
    {
        FiveHour = five.HasValue
            ? new RateLimitWindow { UsedPercentage = five.Value, ResetsAt = Now.ToUnixTimeSeconds() + fiveResetsIn }
            : null,
        SevenDay = seven.HasValue
            ? new RateLimitWindow { UsedPercentage = seven.Value, ResetsAt = Now.ToUnixTimeSeconds() + sevenResetsIn }
            : null,
    };

    private static BindingWindow Capped(string label = "5H") => new(label, TimeSpan.FromHours(1));

    // ---- Binding: which window is actually holding work back -------------

    [Fact]
    public void AbsentRateLimitsBindNothing()
    {
        // §9.1: on an API key there are no windows to test, so no escalation can fire.
        var binding = SpendSlot.Binding(null, Now);

        Assert.Null(binding);
    }

    [Fact]
    public void RateLimitsCarryingNoWindowsBindNothing()
    {
        var binding = SpendSlot.Binding(Limits(), Now);

        Assert.Null(binding);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(99.9)]
    public void WindowsBelowTheirCapBindNothing(double pct)
    {
        var limits = Limits(five: pct, seven: pct);

        var binding = SpendSlot.Binding(limits, Now);

        Assert.Null(binding);
    }

    [Fact]
    public void AFullFiveHourWindowBinds()
    {
        var binding = SpendSlot.Binding(Limits(five: 100, seven: 44), Now);

        Assert.NotNull(binding);
        Assert.Equal("5H", binding.Label);
        Assert.Equal(TimeSpan.FromHours(1), binding.Remaining);
    }

    [Fact]
    public void AFullSevenDayWindowBindsEvenWhileTheFiveHourWindowIsNearlyEmpty()
    {
        // The reported defect: the weekly window is what gates work here, and
        // keying the slot off the 5h window alone left it entirely unreported.
        var binding = SpendSlot.Binding(Limits(five: 5, seven: 100), Now);

        Assert.NotNull(binding);
        Assert.Equal("7D", binding.Label);
        Assert.Equal(TimeSpan.FromDays(1), binding.Remaining);
    }

    [Fact]
    public void WithBothWindowsFullTheLaterResetBinds()
    {
        var binding = SpendSlot.Binding(Limits(five: 100, seven: 100), Now);

        Assert.NotNull(binding);
        Assert.Equal("7D", binding.Label);
        Assert.Equal(TimeSpan.FromDays(1), binding.Remaining);
    }

    [Fact]
    public void TheLaterResetBindsRegardlessOfWhichWindowItBelongsTo()
    {
        // "Latest reset", not "7d always wins" -- a 7d window minutes from
        // rolling over frees work sooner than a 5h one that just filled.
        var binding = SpendSlot.Binding(
            Limits(five: 100, seven: 100, fiveResetsIn: 4 * 3600, sevenResetsIn: 600), Now);

        Assert.NotNull(binding);
        Assert.Equal("5H", binding.Label);
        Assert.Equal(TimeSpan.FromHours(4), binding.Remaining);
    }

    [Theory]
    [InlineData("5H")]
    [InlineData("7D")]
    public void AboveTheCapBindsJustLikeAtIt(string window)
    {
        var limits = window is "5H" ? Limits(five: 150) : Limits(seven: 150);

        var binding = SpendSlot.Binding(limits, Now);

        Assert.NotNull(binding);
        Assert.Equal(window, binding.Label);
    }

    // ---- Determine: which tenant holds the slot --------------------------

    [Fact]
    public void NothingBindingIsDimEstimateEvenWithCreditsAvailable()
    {
        var kind = SpendSlot.Determine(null, Usage(true, 100), sessionCreditsMinor: 0);

        Assert.Equal(SpendSlotKind.DimEstimate, kind);
    }

    [Fact]
    public void AtCapWithCreditsEnabledAndFigureIsCredits()
    {
        Assert.Equal(SpendSlotKind.Credits, SpendSlot.Determine(Capped(), Usage(true, 134), 34));
    }

    [Fact]
    public void AtCapWithCreditsEnabledButNoFigureIsAtLimitNotCredits()
    {
        // §6.1 #2: is_enabled:true with no figure to show is "unknown", not credits.
        Assert.Equal(SpendSlotKind.AtLimit, SpendSlot.Determine(Capped(), Usage(true, usedCredits: null), null));
    }

    [Fact]
    public void AtCapWithCreditsDisabledIsBlocked()
    {
        Assert.Equal(SpendSlotKind.Blocked, SpendSlot.Determine(Capped(), Usage(false), null));
    }

    [Fact]
    public void AtCapWithNoUsageAtAllIsAtLimitNotBlocked()
    {
        // The refresher's cache may simply be cold -- must not assert "blocked" without proof.
        Assert.Equal(SpendSlotKind.AtLimit, SpendSlot.Determine(Capped(), null, null));
    }

    [Fact]
    public void BelowTheCapASessionThatHasSpentCreditsKeepsShowingThem()
    {
        // A window rolling over underneath a session must not erase what that
        // session was really billed; the figure stays, the banner does not.
        Assert.Equal(SpendSlotKind.DimCredits, SpendSlot.Determine(null, Usage(true, 2656), 134));
    }

    [Fact]
    public void BelowTheCapASessionThatHasSpentNoCreditsShowsTheEstimate()
    {
        Assert.Equal(SpendSlotKind.DimEstimate, SpendSlot.Determine(null, Usage(true, 2656), 0));
    }

    [Fact]
    public void ColdUsageCacheCostsTheRealFigureNotTheLine()
    {
        // §9.3: the API is never load-bearing. Without a figure the slot falls
        // back to the estimate rather than showing nothing.
        var kind = SpendSlot.Determine(null, null, sessionCreditsMinor: 134);

        Assert.Equal(SpendSlotKind.DimEstimate, kind);
    }

    [Theory]
    [InlineData(false, 2656L)] // switched off: that figure is not ours to claim
    [InlineData(true, null)]   // enabled, but no figure to show yet
    public void DimCreditsNeedsAnEligibleCreditsFigureNotMerelyASessionDelta(bool isEnabled, long? usedCredits)
    {
        var usage = Usage(isEnabled, usedCredits);

        var kind = SpendSlot.Determine(null, usage, sessionCreditsMinor: 134);

        Assert.Equal(SpendSlotKind.DimEstimate, kind);
    }

    [Fact]
    public void IsCreditsEligibleRequiresBothFlagAndFigure()
    {
        Assert.True(SpendSlot.IsCreditsEligible(Usage(true, 1)));
        Assert.False(SpendSlot.IsCreditsEligible(Usage(true, null)));
        Assert.False(SpendSlot.IsCreditsEligible(Usage(false, 1)));
        Assert.False(SpendSlot.IsCreditsEligible(null));
    }

    [Theory]
    [InlineData(true, CreditStatus.On)]
    [InlineData(false, CreditStatus.Off)]
    [InlineData(null, CreditStatus.Unknown)]
    public void ResolveCreditStatusMapsDirectly(bool? isEnabled, CreditStatus expected)
    {
        Assert.Equal(expected, SpendSlot.ResolveCreditStatus(isEnabled is null ? null : Usage(isEnabled)));
    }

    // ---- Render ----------------------------------------------------------

    [Fact]
    public void DimEstimateRendersGreyWithApproxPrefixAndNoLabelsWhenCompact()
    {
        var data = new SpendSlotData(0.82, 2.41, "USD", null, null, Binding: null);
        var full = new AnsiBuilder();
        SpendSlot.Render(full, SpendSlotKind.DimEstimate, data, Icons, compact: false);
        var fullText = TestSupport.StripAnsi(full.Build());
        Assert.Contains("≈$0.82 session", fullText);
        Assert.Contains("≈$2.41 today", fullText);

        var compact = new AnsiBuilder();
        SpendSlot.Render(compact, SpendSlotKind.DimEstimate, data, Icons, compact: true);
        var compactText = TestSupport.StripAnsi(compact.Build());
        Assert.DoesNotContain("session", compactText);
        Assert.DoesNotContain("today", compactText);
        Assert.Contains("≈$0.82", compactText);
        Assert.Contains("≈$2.41", compactText);
    }

    [Fact]
    public void CreditsRendersRealFiguresNeverApprox()
    {
        var data = new SpendSlotData(0, 0, "EUR", 134, 410, Binding: Capped());
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.Credits, data, Icons, compact: false);
        var text = TestSupport.StripAnsi(b.Build());

        Assert.Contains("CREDITS", text);
        Assert.Contains("€1.34", text);
        Assert.Contains("€4.10", text);
        Assert.DoesNotContain("≈", text);
    }

    [Fact]
    public void DimCreditsRendersTheSameRealFiguresWithoutTheBanner()
    {
        var data = new SpendSlotData(3.42, 8.10, "EUR", 134, 410, Binding: null);
        var b = new AnsiBuilder();

        SpendSlot.Render(b, SpendSlotKind.DimCredits, data, Icons, compact: false);

        var text = TestSupport.StripAnsi(b.Build());
        Assert.Equal($"{Icons.Credits} €1.34 session · €4.10 today", text);
        Assert.DoesNotContain("CREDITS", text);
        Assert.DoesNotContain("≈", text);
        Assert.DoesNotContain("3.42", text); // the estimate it replaces
    }

    [Fact]
    public void DimCreditsShedsItsWordsWhenCompact()
    {
        var data = new SpendSlotData(3.42, 8.10, "EUR", 134, 410, Binding: null);
        var b = new AnsiBuilder();

        SpendSlot.Render(b, SpendSlotKind.DimCredits, data, Icons, compact: true);

        Assert.Equal($"{Icons.Credits} €1.34 · €4.10", TestSupport.StripAnsi(b.Build()));
    }

    [Fact]
    public void BlockedShowsCountdownToTheBindingWindowsReset()
    {
        var data = new SpendSlotData(2.5, 2.5, "USD", null, null, new BindingWindow("7D", TimeSpan.FromMinutes(8)));
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.Blocked, data, Icons, compact: false);
        var text = TestSupport.StripAnsi(b.Build());

        Assert.Contains("LIMIT REACHED", text);
        Assert.Contains("0h08m", text);
    }

    [Fact]
    public void BlockedCountsDownAcrossDaysWhenTheWeeklyWindowBinds()
    {
        var data = new SpendSlotData(2.5, 2.5, "USD", null, null, new BindingWindow("7D", TimeSpan.FromHours(40)));
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.Blocked, data, Icons, compact: false);

        Assert.Contains("1d16h", TestSupport.StripAnsi(b.Build()));
    }

    [Fact]
    public void AtLimitKeepsOnlyTheSessionEstimate()
    {
        var data = new SpendSlotData(2.5, 9.99, "USD", null, null, Capped());
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.AtLimit, data, Icons, compact: false);
        var text = TestSupport.StripAnsi(b.Build());

        Assert.Contains("5H LIMIT", text);
        Assert.Contains("≈$2.50 session", text);
        Assert.DoesNotContain("9.99", text); // today's figure is not shown in this state (§6.1 #3)
    }

    [Fact]
    public void AtLimitNamesTheWindowThatIsActuallyFull()
    {
        var data = new SpendSlotData(2.5, 9.99, "USD", null, null, Capped("7D"));
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.AtLimit, data, Icons, compact: false);
        var text = TestSupport.StripAnsi(b.Build());

        Assert.Contains("7D LIMIT", text);
        Assert.DoesNotContain("5H", text);
    }

    [Theory]
    [InlineData(SpendSlotKind.DimEstimate)]
    [InlineData(SpendSlotKind.DimCredits)]
    [InlineData(SpendSlotKind.AtLimit)]
    [InlineData(SpendSlotKind.Blocked)]
    [InlineData(SpendSlotKind.Credits)]
    public void AsciiIconSetRendersEverySlotWithoutNonAsciiCharacters(SpendSlotKind kind)
    {
        // §5.2's ascii set exists for terminals that can't draw anything else,
        // so the money figures and their separator have to fall back too --
        // they come from §5.6's formatting, not from the icon table.
        var data = new SpendSlotData(0.82, 2.41, "EUR", 134, 410, new BindingWindow("7D", TimeSpan.FromMinutes(8)));
        foreach (var compact in new[] { false, true })
        {
            var b = new AnsiBuilder();
            SpendSlot.Render(b, kind, data, AsciiIcons, compact);
            var text = TestSupport.StripAnsi(b.Build());
            Assert.True(System.Text.Ascii.IsValid(text), $"{kind} (compact: {compact}) rendered non-ASCII: {text}");
        }
    }

    [Fact]
    public void AsciiDimEstimateKeepsItsFiguresAndSeparator()
    {
        var data = new SpendSlotData(0.82, 2.41, "USD", null, null, Binding: null);
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.DimEstimate, data, AsciiIcons, compact: false);
        var text = TestSupport.StripAnsi(b.Build());

        Assert.Equal("$ ~$0.82 session | ~$2.41 today", text);
    }

    [Fact]
    public void AsciiDimCreditsKeepsItsFiguresAndSeparator()
    {
        var data = new SpendSlotData(0.82, 2.41, "EUR", 134, 410, Binding: null);
        var b = new AnsiBuilder();

        SpendSlot.Render(b, SpendSlotKind.DimCredits, data, AsciiIcons, compact: false);

        Assert.Equal("! EUR 1.34 session | EUR 4.10 today", TestSupport.StripAnsi(b.Build()));
    }
}
