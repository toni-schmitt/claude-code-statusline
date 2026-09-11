using Ember.Core.Data;
using Ember.Core.Render;

namespace Ember.Core;

public enum SpendSlotKind
{
    DimEstimate,
    AtLimit,
    Blocked,
    Credits,
}

public enum CreditStatus
{
    Unknown,
    On,
    Off,
}

/// <summary>Everything <see cref="SpendSlot.Render"/> needs, pre-resolved so it never touches currency or watermark math directly.</summary>
public sealed record SpendSlotData(
    double DimSessionUsd,
    double DimTodayUsd,
    string CreditsCurrency,
    long? CreditsSessionMinor,
    long? CreditsTodayMinor,
    TimeSpan FiveHourRemaining);

/// <summary>
/// The §6 state machine. Memoryless: every render recomputes the tenant
/// fresh from the current 5h percentage (stdin, always fresh, §9.3) and the
/// refresher's cached usage (API-sourced, may be stale or absent). §6.1's
/// "escalates the instant is_enabled: false is confirmed" falls out for
/// free -- there is no persisted "we were blocked last time" flag, so the
/// very next render that sees the flipped cache value picks a different
/// branch automatically.
/// </summary>
public static class SpendSlot
{
    /// <summary>§6.1 #2: <c>is_enabled: true</c> with no figure to show is <c>unknown</c>, not credits.</summary>
    public static bool IsCreditsEligible(UsageResponse? usage) =>
        usage?.ExtraUsage is { IsEnabled: true, UsedCredits: not null };

    public static CreditStatus ResolveCreditStatus(UsageResponse? usage) => usage?.ExtraUsage?.IsEnabled switch
    {
        true => CreditStatus.On,
        false => CreditStatus.Off,
        null => CreditStatus.Unknown,
    };

    /// <summary>
    /// §6's table. <paramref name="fiveHourUsedPercentage"/> must come from
    /// stdin, never from the usage API -- §9.3's "no API-sourced segment may
    /// be load-bearing" is enforced structurally here: the API only ever
    /// decides which flavour of an already-stdin-proven "at the cap" state
    /// to show, never whether the cap was reached at all.
    /// </summary>
    public static SpendSlotKind Determine(double? fiveHourUsedPercentage, UsageResponse? usage)
    {
        if (fiveHourUsedPercentage is not double five || five < 100) return SpendSlotKind.DimEstimate;
        if (IsCreditsEligible(usage)) return SpendSlotKind.Credits;
        return ResolveCreditStatus(usage) == CreditStatus.Off ? SpendSlotKind.Blocked : SpendSlotKind.AtLimit;
    }

    public static void Render(AnsiBuilder b, SpendSlotKind kind, SpendSlotData d, IconGlyphs icons, bool compact)
    {
        switch (kind)
        {
            case SpendSlotKind.DimEstimate: RenderDim(b, d, icons, compact); break;
            case SpendSlotKind.AtLimit: RenderAtLimit(b, d, icons); break;
            case SpendSlotKind.Blocked: RenderBlocked(b, d, icons); break;
            case SpendSlotKind.Credits: RenderCredits(b, d, icons, compact); break;
        }
    }

    /// <summary>§6.1 #1 -- entirely grey 245, including its own icon.</summary>
    private static void RenderDim(AnsiBuilder b, SpendSlotData d, IconGlyphs icons, bool compact)
    {
        var session = Format.Money(d.DimSessionUsd, "USD", estimate: true);
        var today = Format.Money(d.DimTodayUsd, "USD", estimate: true);
        var text = compact
            ? $"{icons.Spend} {session} · {today}"
            : $"{icons.Spend} {session} session · {today} today";
        b.Colored(text, Palette.Label);
    }

    /// <summary>§6.1 #3 -- reverse video on warn (180); keeps only the session estimate, the one figure this state can still stand behind.</summary>
    private static void RenderAtLimit(AnsiBuilder b, SpendSlotData d, IconGlyphs icons)
    {
        b.ReverseBanner($"{icons.Blocked} 5H LIMIT", Palette.Warn).Raw(" ");
        b.Colored($"{Format.Money(d.DimSessionUsd, "USD", estimate: true)} session", Palette.Label);
    }

    /// <summary>§6.1 #4 -- reverse video on critical (203); the countdown reuses the same reset the 5h bar shows.</summary>
    private static void RenderBlocked(AnsiBuilder b, SpendSlotData d, IconGlyphs icons)
    {
        b.ReverseBanner($"{icons.Blocked} LIMIT REACHED", Palette.Critical).Raw(" ");
        b.Colored("resets ", Palette.Label);
        var countdown = Format.Countdown(d.FiveHourRemaining);
        if (countdown.Length > 0) b.Colored(countdown, Palette.Critical, bold: true);
    }

    /// <summary>§6.1 #2 -- reverse video on session-share ember (202); real figures only, never "&#8776;".</summary>
    private static void RenderCredits(AnsiBuilder b, SpendSlotData d, IconGlyphs icons, bool compact)
    {
        b.ReverseBanner($"{icons.Credits} CREDITS", Palette.SessionShare).Raw(" ");

        var sessionUsd = Format.MinorUnitsToMajor(d.CreditsSessionMinor ?? 0, d.CreditsCurrency);
        var todayUsd = Format.MinorUnitsToMajor(d.CreditsTodayMinor ?? 0, d.CreditsCurrency);
        var session = Format.Money(sessionUsd, d.CreditsCurrency, estimate: false);
        var today = Format.Money(todayUsd, d.CreditsCurrency, estimate: false);

        b.Colored(session, Palette.GradientStop1, bold: true);
        b.Colored(compact ? " · " : " session · ", Palette.Label);
        b.Colored(today, Palette.GradientStop1, bold: true);
        if (!compact) b.Colored(" today", Palette.Label);
    }
}
