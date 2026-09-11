using Ember.Core.Data;

namespace Ember.Core;

/// <summary>
/// The three §7 watermark-relative computations, plus the small bookkeeping
/// around them. Every function is pure with respect to time -- callers pass
/// in <c>localDate</c>/<c>nowEpochSeconds</c> rather than this file reading
/// the clock -- so the render entry point is the only place that reads
/// wall-clock time, and this file stays unit-testable without mocking it.
///
/// §7.2 and §7.3 both key off "did the local day change", but they must
/// each see that signal exactly once even though they run one after the
/// other in the same render. <see cref="HasLocalDayChanged"/> only reads
/// <c>account.Day</c>; <see cref="CommitLocalDay"/> is the only function
/// that writes it, and must run after both §7.2 and §7.3 have consumed the
/// flag (see the call order documented on <see cref="CommitLocalDay"/>).
/// </summary>
public static class Derived
{
    /// <summary>Looks up or creates this session's watermark record. <paramref name="isNewSession"/> drives §7.1/§7.2's "first sight" baseline.</summary>
    public static SessionState GetOrCreateSession(AccountState account, string sessionId, out bool isNewSession)
    {
        if (account.Sessions.TryGetValue(sessionId, out var existing))
        {
            isNewSession = false;
            return existing;
        }

        var created = new SessionState();
        account.Sessions[sessionId] = created;
        isNewSession = true;
        return created;
    }

    /// <summary>
    /// §7.1. Mutates <paramref name="session"/>'s watermark in place. Returns
    /// the clamped share, or null when there is no five_hour window to
    /// compare against at all (§9.1: no escalation can fire).
    ///
    /// A freshly created <see cref="SessionState"/> defaults
    /// <c>FiveHourResetsAt</c> to 0, which no real epoch-second
    /// <c>resets_at</c> will ever equal, so a single not-equal check covers
    /// both "first sight of this session" and "the window rolled over"
    /// without a separate flag.
    /// </summary>
    public static double? FiveHourShare(SessionState session, RateLimitWindow? fiveHour)
    {
        if (fiveHour is null) return null;

        if (session.FiveHourResetsAt != fiveHour.ResetsAt)
        {
            session.FiveHourWatermark = fiveHour.UsedPercentage;
            session.FiveHourResetsAt = fiveHour.ResetsAt;
        }

        return Math.Max(0, fiveHour.UsedPercentage - session.FiveHourWatermark);
    }

    /// <summary>Has the local day changed since the state file was last written? Read-only. Call once, before <see cref="Credits"/> and <see cref="AccumulateSpendEstimate"/>.</summary>
    public static bool HasLocalDayChanged(AccountState account, string localDate) => account.Day != localDate;

    /// <summary>
    /// §7.2, minor units. Null when <paramref name="usedCreditsMinor"/>
    /// itself is null (credits not known this render -- the caller must
    /// still call <see cref="CommitLocalDay"/>). <c>day_start_credits</c>
    /// and <c>session_start_credits</c> each independently re-baseline
    /// against their own drop check, per §7.2's wording.
    /// </summary>
    public static (long SessionCredits, long TodayCredits)? Credits(
        AccountState account, SessionState session, bool isNewSession, bool dayChanged, long? usedCreditsMinor)
    {
        if (usedCreditsMinor is not long used) return null;

        if (dayChanged || used < account.DayStartCredits) account.DayStartCredits = used;
        if (isNewSession || used < session.SessionStartCredits) session.SessionStartCredits = used;

        return (Math.Max(0, used - session.SessionStartCredits), Math.Max(0, used - account.DayStartCredits));
    }

    /// <summary>§7.3. Mutates <paramref name="account"/>'s running total and <paramref name="session"/>'s last-seen cost in place.</summary>
    public static void AccumulateSpendEstimate(AccountState account, SessionState session, bool dayChanged, double totalCostUsd)
    {
        if (dayChanged) account.TodayEstimateUsd = 0;

        double delta = Math.Max(0, totalCostUsd - session.LastCostUsd);
        account.TodayEstimateUsd += delta;
        session.LastCostUsd = totalCostUsd;
    }

    /// <summary>Commits the local day. Call once, after both <see cref="Credits"/> and <see cref="AccumulateSpendEstimate"/> have consumed the "did the day change" flag.</summary>
    public static void CommitLocalDay(AccountState account, string localDate) => account.Day = localDate;

    /// <summary>Stamps <c>seen_at</c> for the current session and drops sessions unseen for more than 24h (§7.3). Call last, before <c>StateStore.Save</c>.</summary>
    public static void TouchAndPruneSessions(AccountState account, string currentSessionId, long nowEpochSeconds)
    {
        if (account.Sessions.TryGetValue(currentSessionId, out var current))
        {
            current.SeenAt = nowEpochSeconds;
        }

        const long pruneAfterSeconds = 24 * 3600;
        var stale = account.Sessions
            .Where(kv => kv.Key != currentSessionId && nowEpochSeconds - kv.Value.SeenAt > pruneAfterSeconds)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in stale)
        {
            account.Sessions.Remove(key);
        }
    }
}
