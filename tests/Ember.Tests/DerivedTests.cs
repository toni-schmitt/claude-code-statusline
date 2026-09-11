using Ember.Core;
using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

/// <summary>§7's three watermark-relative computations.</summary>
public class DerivedTests
{
    [Fact]
    public void GetOrCreateSessionReportsNewOnlyOnce()
    {
        var account = new AccountState();

        Derived.GetOrCreateSession(account, "s1", out var firstIsNew);
        Assert.True(firstIsNew);

        Derived.GetOrCreateSession(account, "s1", out var secondIsNew);
        Assert.False(secondIsNew);
    }

    [Fact]
    public void FiveHourShareIsZeroOnFirstSight()
    {
        var session = new SessionState();
        var window = new RateLimitWindow { UsedPercentage = 63, ResetsAt = 1000 };

        var share = Derived.FiveHourShare(session, window);

        Assert.Equal(0, share);
        Assert.Equal(63, session.FiveHourWatermark);
        Assert.Equal(1000, session.FiveHourResetsAt);
    }

    [Fact]
    public void FiveHourShareGrowsAgainstTheWatermark()
    {
        var session = new SessionState();
        Derived.FiveHourShare(session, new RateLimitWindow { UsedPercentage = 63, ResetsAt = 1000 });

        var share = Derived.FiveHourShare(session, new RateLimitWindow { UsedPercentage = 85, ResetsAt = 1000 });

        Assert.Equal(22, share); // the spec's own worked example: 63 -> 85 is +22%
    }

    [Fact]
    public void FiveHourShareClampsAtZeroWhenUsageFalls()
    {
        var session = new SessionState();
        Derived.FiveHourShare(session, new RateLimitWindow { UsedPercentage = 63, ResetsAt = 1000 });

        var share = Derived.FiveHourShare(session, new RateLimitWindow { UsedPercentage = 40, ResetsAt = 1000 });

        Assert.Equal(0, share); // another machine/tab drawing on the same account can make the total fall (§7.1)
    }

    [Fact]
    public void FiveHourShareResetsWatermarkWhenWindowRollsOver()
    {
        var session = new SessionState();
        Derived.FiveHourShare(session, new RateLimitWindow { UsedPercentage = 90, ResetsAt = 1000 });

        // New window (different resets_at): watermark rebaselines even though usage dropped.
        var share = Derived.FiveHourShare(session, new RateLimitWindow { UsedPercentage = 10, ResetsAt = 2000 });

        Assert.Equal(0, share);
        Assert.Equal(10, session.FiveHourWatermark);
        Assert.Equal(2000, session.FiveHourResetsAt);
    }

    [Fact]
    public void FiveHourShareIsNullWithNoWindow()
    {
        Assert.Null(Derived.FiveHourShare(new SessionState(), null));
    }

    [Fact]
    public void HasLocalDayChangedComparesAccountDay()
    {
        var account = new AccountState { Day = "2026-09-10" };
        Assert.True(Derived.HasLocalDayChanged(account, "2026-09-11"));
        Assert.False(Derived.HasLocalDayChanged(account, "2026-09-10"));
    }

    [Fact]
    public void CreditsIsNullWhenUsedCreditsUnknown()
    {
        var account = new AccountState();
        var session = new SessionState();
        Assert.Null(Derived.Credits(account, session, isNewSession: true, dayChanged: false, usedCreditsMinor: null));
    }

    [Fact]
    public void CreditsBaselinesOnNewSessionAndNewDay()
    {
        var account = new AccountState();
        var session = new SessionState();

        var result = Derived.Credits(account, session, isNewSession: true, dayChanged: true, usedCreditsMinor: 134);

        Assert.NotNull(result);
        Assert.Equal((0L, 0L), result.Value);
        Assert.Equal(134, account.DayStartCredits);
        Assert.Equal(134, session.SessionStartCredits);
    }

    [Fact]
    public void CreditsAccumulatesAgainstEstablishedBaselines()
    {
        var account = new AccountState { DayStartCredits = 100 };
        var session = new SessionState { SessionStartCredits = 120 };

        var result = Derived.Credits(account, session, isNewSession: false, dayChanged: false, usedCreditsMinor: 180);

        Assert.NotNull(result);
        Assert.Equal((60L, 80L), result.Value); // session: 180-120, today: 180-100
    }

    [Fact]
    public void CreditsRebaselinesOnMonthRolloverDrop()
    {
        // used_credits dropped below both watermarks: e.g. the 1st of the month.
        var account = new AccountState { DayStartCredits = 500 };
        var session = new SessionState { SessionStartCredits = 500 };

        var result = Derived.Credits(account, session, isNewSession: false, dayChanged: false, usedCreditsMinor: 20);

        Assert.NotNull(result);
        Assert.Equal((0L, 0L), result.Value);
        Assert.Equal(20, account.DayStartCredits);
        Assert.Equal(20, session.SessionStartCredits);
    }

    [Fact]
    public void CreditsBaselinesOnFirstSightNotSessionCreate()
    {
        var account = new AccountState();
        var session = Derived.GetOrCreateSession(account, "s1", out var isNew);
        Assert.True(isNew);

        // Render 1: session just created, but no credits available yet.
        var r1 = Derived.Credits(account, session, isNewSession: true, dayChanged: true, usedCreditsMinor: null);
        Assert.Null(r1);

        // Render 2: session already exists, credits arrive at 5000 (month-to-date).
        // Without the fix, this would show 5000 as session/today credits.
        Derived.GetOrCreateSession(account, "s1", out var isNew2);
        Assert.False(isNew2);
        var r2 = Derived.Credits(account, session, isNewSession: false, dayChanged: false, usedCreditsMinor: 5000);

        Assert.NotNull(r2);
        Assert.Equal((0L, 0L), r2.Value);
        Assert.Equal(5000, session.SessionStartCredits);
        Assert.Equal(5000, account.DayStartCredits);
    }

    [Fact]
    public void CreditsBaselinesOnFirstSightAfterDayChange()
    {
        var account = new AccountState { Day = "2026-09-10", DayStartCredits = 3000 };
        var session = new SessionState { SessionStartCredits = 3000 };

        // Render on new day, but credits unavailable this render.
        var r1 = Derived.Credits(account, session, isNewSession: false, dayChanged: true, usedCreditsMinor: null);
        Assert.Null(r1);

        // Next render (same day), credits arrive.
        var r2 = Derived.Credits(account, session, isNewSession: false, dayChanged: false, usedCreditsMinor: 4000);
        Assert.NotNull(r2);
        Assert.Equal(0L, r2.Value.TodayCredits);
        Assert.Equal(4000, account.DayStartCredits);
    }

    [Fact]
    public void AccumulateSpendEstimateSumsPositiveDeltas()
    {
        var account = new AccountState();
        var session = new SessionState();

        Derived.AccumulateSpendEstimate(account, session, dayChanged: false, totalCostUsd: 0.40);
        Derived.AccumulateSpendEstimate(account, session, dayChanged: false, totalCostUsd: 0.82);

        Assert.Equal(0.82, account.TodayEstimateUsd, precision: 10);
        Assert.Equal(0.82, session.LastCostUsd);
    }

    [Fact]
    public void AccumulateSpendEstimateResetsOnDayChange()
    {
        // A session spanning midnight: yesterday's running total ($5) is wiped at the day
        // boundary, and only the delta since the session's own last-seen cost counts today.
        var account = new AccountState { TodayEstimateUsd = 5.0 };
        var session = new SessionState { LastCostUsd = 2.0 };

        Derived.AccumulateSpendEstimate(account, session, dayChanged: true, totalCostUsd: 2.5);

        Assert.Equal(0.5, account.TodayEstimateUsd, precision: 10); // reset to 0, then + (2.5 - 2.0)
        Assert.Equal(2.5, session.LastCostUsd);
    }

    [Fact]
    public void AccumulateSpendEstimateClampsNegativeDeltaToZero()
    {
        // cost.total_cost_usd resets to zero on /clear (§7.3); a same-session drop must not go negative.
        var account = new AccountState();
        var session = new SessionState { LastCostUsd = 5.0 };

        Derived.AccumulateSpendEstimate(account, session, dayChanged: false, totalCostUsd: 0.0);

        Assert.Equal(0, account.TodayEstimateUsd);
        Assert.Equal(0, session.LastCostUsd);
    }

    [Fact]
    public void TouchAndPruneSessionsTouchesCurrentAndDropsStaleOnes()
    {
        var account = new AccountState
        {
            Sessions =
            {
                ["current"] = new SessionState { SeenAt = 0 },
                ["stale"] = new SessionState { SeenAt = 0 },
                ["recent"] = new SessionState { SeenAt = 100 },
            },
        };
        long now = 25 * 3600; // 25 hours later

        Derived.TouchAndPruneSessions(account, "current", now);

        Assert.True(account.Sessions.ContainsKey("current"));
        Assert.Equal(now, account.Sessions["current"].SeenAt);
        Assert.False(account.Sessions.ContainsKey("stale")); // unseen > 24h, pruned
        Assert.False(account.Sessions.ContainsKey("recent")); // also unseen > 24h relative to "now"
    }

    [Fact]
    public void TouchAndPruneSessionsKeepsSessionsWithinTheWindow()
    {
        var account = new AccountState
        {
            Sessions = { ["fresh"] = new SessionState { SeenAt = 0 } },
        };

        Derived.TouchAndPruneSessions(account, "current", nowEpochSeconds: 3600); // 1 hour later

        Assert.True(account.Sessions.ContainsKey("fresh"));
    }
}
