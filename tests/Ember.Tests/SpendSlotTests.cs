using Ember.Core;
using Ember.Core.Data;
using Ember.Core.Render;
using Xunit;

namespace Ember.Tests;

/// <summary>§6's state machine and its four render tenants.</summary>
public class SpendSlotTests
{
    private static readonly IconGlyphs Icons = Ember.Core.Render.Icons.For(IconSet.Nerd);

    private static UsageResponse Usage(bool? isEnabled, long? usedCredits = null) => new()
    {
        ExtraUsage = new ExtraUsage { IsEnabled = isEnabled, UsedCredits = usedCredits },
    };

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(99.9)]
    public void BelowCapIsAlwaysDimEstimateRegardlessOfUsage(double? pct)
    {
        Assert.Equal(SpendSlotKind.DimEstimate, SpendSlot.Determine(pct, Usage(true, 100)));
    }

    [Fact]
    public void NoRateLimitsAtAllIsDimEstimate()
    {
        // §9.1: on an API key, there is no 5h to test, so no escalation can fire.
        Assert.Equal(SpendSlotKind.DimEstimate, SpendSlot.Determine(null, null));
    }

    [Fact]
    public void AtCapWithCreditsEnabledAndFigureIsCredits()
    {
        Assert.Equal(SpendSlotKind.Credits, SpendSlot.Determine(100, Usage(true, 134)));
    }

    [Fact]
    public void AtCapWithCreditsEnabledButNoFigureIsAtLimitNotCredits()
    {
        // §6.1 #2: is_enabled:true with no figure to show is "unknown", not credits.
        Assert.Equal(SpendSlotKind.AtLimit, SpendSlot.Determine(100, Usage(true, usedCredits: null)));
    }

    [Fact]
    public void AtCapWithCreditsDisabledIsBlocked()
    {
        Assert.Equal(SpendSlotKind.Blocked, SpendSlot.Determine(100, Usage(false)));
    }

    [Fact]
    public void AtCapWithNoUsageAtAllIsAtLimitNotBlocked()
    {
        // The refresher's cache may simply be cold -- must not assert "blocked" without proof.
        Assert.Equal(SpendSlotKind.AtLimit, SpendSlot.Determine(100, null));
    }

    [Fact]
    public void AboveCapBehavesLikeAtCap()
    {
        Assert.Equal(SpendSlotKind.Blocked, SpendSlot.Determine(150, Usage(false)));
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

    [Fact]
    public void DimEstimateRendersGreyWithApproxPrefixAndNoLabelsWhenCompact()
    {
        var data = new SpendSlotData(0.82, 2.41, "USD", null, null, TimeSpan.Zero);
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
        var data = new SpendSlotData(0, 0, "EUR", 134, 410, TimeSpan.Zero);
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.Credits, data, Icons, compact: false);
        var text = TestSupport.StripAnsi(b.Build());

        Assert.Contains("CREDITS", text);
        Assert.Contains("€1.34", text);
        Assert.Contains("€4.10", text);
        Assert.DoesNotContain("≈", text);
    }

    [Fact]
    public void BlockedShowsCountdownToReset()
    {
        var data = new SpendSlotData(2.5, 2.5, "USD", null, null, TimeSpan.FromMinutes(8));
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.Blocked, data, Icons, compact: false);
        var text = TestSupport.StripAnsi(b.Build());

        Assert.Contains("LIMIT REACHED", text);
        Assert.Contains("0h08m", text);
    }

    [Fact]
    public void AtLimitKeepsOnlyTheSessionEstimate()
    {
        var data = new SpendSlotData(2.5, 9.99, "USD", null, null, TimeSpan.Zero);
        var b = new AnsiBuilder();
        SpendSlot.Render(b, SpendSlotKind.AtLimit, data, Icons, compact: false);
        var text = TestSupport.StripAnsi(b.Build());

        Assert.Contains("5H LIMIT", text);
        Assert.Contains("≈$2.50 session", text);
        Assert.DoesNotContain("9.99", text); // today's figure is not shown in this state (§6.1 #3)
    }
}
