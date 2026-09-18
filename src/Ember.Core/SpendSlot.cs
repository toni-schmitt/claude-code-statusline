using Ember.Core.Data;
using Ember.Core.Render;

namespace Ember.Core;

public enum SpendSlotKind
{
    DimEstimate,
    DimCredits,
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

/// <summary>
/// The rate-limit window that is holding work back: its short name for the
/// banner, and how long until it lets go. <paramref name="Label"/> is already
/// upper-case, as the banners render it.
/// </summary>
public sealed record BindingWindow(string Label, TimeSpan Remaining);

/// <summary>Everything <see cref="SpendSlot.Render"/> needs, pre-resolved so it never touches currency or watermark math directly.</summary>
public sealed record SpendSlotData(
    double DimSessionUsd,
    double DimTodayUsd,
    string CreditsCurrency,
    long? CreditsSessionMinor,
    long? CreditsTodayMinor,
    BindingWindow? Binding);

/// <summary>
/// The §6 state machine. Memoryless: every render recomputes the tenant
/// fresh from the current window percentages (stdin, always fresh, §9.3) and
/// the refresher's cached usage (API-sourced, may be stale or absent). §6.1's
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
    /// The window actually holding work back: of those at or past their cap,
    /// the one that resets last. A full 5h window with a full 7d window behind
    /// it frees nothing when it rolls over, so the later reset is the one the
    /// banners must name and count down to.
    ///
    /// Both windows arrive on stdin, so §9.3 holds structurally: the API never
    /// gets a say in whether a cap was reached, only in what happens next.
    /// Returns null when nothing is capped -- §9.1's "no rate_limits at all"
    /// lands here too, and no escalation can fire.
    /// </summary>
    public static BindingWindow? Binding(RateLimitsInfo? limits, DateTimeOffset now)
    {
        var five = AtCap("5H", limits?.FiveHour, now);
        var seven = AtCap("7D", limits?.SevenDay, now);

        if (five is null) return seven;
        if (seven is null) return five;
        return seven.Remaining >= five.Remaining ? seven : five;
    }

    /// <summary>
    /// <paramref name="window"/> as a <see cref="BindingWindow"/> if it is at or past
    /// its cap, otherwise null. <paramref name="label"/> is the short name the banners
    /// print. <paramref name="now"/> is passed in rather than read so a render's two
    /// windows are measured against one instant.
    /// </summary>
    private static BindingWindow? AtCap(string label, RateLimitWindow? window, DateTimeOffset now)
    {
        if (window is null) return null;
        if (window.UsedPercentage < 100) return null;

        return new BindingWindow(label, DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt) - now);
    }

    /// <summary>
    /// §6's table. <paramref name="binding"/> must come from <see cref="Binding"/>,
    /// which reads stdin only -- §9.3's "no API-sourced segment may be load-bearing"
    /// is enforced structurally here: the API only ever decides which flavour of an
    /// already-stdin-proven "at the cap" state to show, never whether the cap was
    /// reached at all.
    ///
    /// <paramref name="sessionCreditsMinor"/> is §7.2's session delta. Below the cap
    /// it is what separates a session that has really been billed for credits from
    /// one that has not: the former keeps showing real money for the rest of its
    /// life, the latter shows the list-price estimate.
    /// </summary>
    public static SpendSlotKind Determine(BindingWindow? binding, UsageResponse? usage, long? sessionCreditsMinor)
    {
        if (binding is null)
        {
            return IsCreditsEligible(usage) && sessionCreditsMinor is > 0
                ? SpendSlotKind.DimCredits
                : SpendSlotKind.DimEstimate;
        }

        if (IsCreditsEligible(usage)) return SpendSlotKind.Credits;
        return ResolveCreditStatus(usage) == CreditStatus.Off ? SpendSlotKind.Blocked : SpendSlotKind.AtLimit;
    }

    public static void Render(AnsiBuilder b, SpendSlotKind kind, SpendSlotData d, IconGlyphs icons, bool compact)
    {
        switch (kind)
        {
            case SpendSlotKind.DimEstimate: RenderDim(b, d, icons, compact); break;
            case SpendSlotKind.DimCredits: RenderDimCredits(b, d, icons, compact); break;
            case SpendSlotKind.AtLimit: RenderAtLimit(b, d, icons); break;
            case SpendSlotKind.Blocked: RenderBlocked(b, d, icons); break;
            case SpendSlotKind.Credits: RenderCredits(b, d, icons, compact); break;
        }
    }

    /// <summary>§6.1 #1 -- entirely grey 245, including its own icon.</summary>
    private static void RenderDim(AnsiBuilder b, SpendSlotData d, IconGlyphs icons, bool compact)
    {
        var session = Format.Money(d.DimSessionUsd, "USD", estimate: true, icons.Set);
        var today = Format.Money(d.DimTodayUsd, "USD", estimate: true, icons.Set);
        var dot = Format.Dot(icons.Set);
        var text = compact
            ? $"{icons.Spend} {session} {dot} {today}"
            : $"{icons.Spend} {session} session {dot} {today} today";
        b.Colored(text, Palette.Label);
    }

    /// <summary>
    /// Real credit figures in the dim estimate's grey, for a session that has been
    /// billed for credits but is no longer against a cap. The reverse-video banner
    /// is reserved for the escalated states -- leaving it lit here would raise an
    /// alarm that is not currently ringing -- but the figures are money that was
    /// really spent, so a session never loses what it actually cost when a window
    /// rolls over underneath it. Carries the credits icon rather than the estimate's
    /// so the two grey states are told apart without reading the numbers.
    /// </summary>
    private static void RenderDimCredits(AnsiBuilder b, SpendSlotData d, IconGlyphs icons, bool compact)
    {
        var sessionMajor = Format.MinorUnitsToMajor(d.CreditsSessionMinor ?? 0, d.CreditsCurrency);
        var todayMajor = Format.MinorUnitsToMajor(d.CreditsTodayMinor ?? 0, d.CreditsCurrency);
        var session = Format.Money(sessionMajor, d.CreditsCurrency, estimate: false, icons.Set);
        var today = Format.Money(todayMajor, d.CreditsCurrency, estimate: false, icons.Set);
        var dot = Format.Dot(icons.Set);
        var text = compact
            ? $"{icons.Credits} {session} {dot} {today}"
            : $"{icons.Credits} {session} session {dot} {today} today";
        b.Colored(text, Palette.Label);
    }

    /// <summary>
    /// §6.1 #3 -- reverse video on warn (180); keeps only the session estimate, the
    /// one figure this state can still stand behind. The banner names the window
    /// that is full, so "wait an hour" and "wait until Friday" do not read alike.
    /// </summary>
    private static void RenderAtLimit(AnsiBuilder b, SpendSlotData d, IconGlyphs icons)
    {
        var label = d.Binding is not null ? $"{d.Binding.Label} LIMIT" : "LIMIT";
        b.ReverseBanner($"{icons.Blocked} {label}", Palette.Warn).Raw(" ");
        b.Colored($"{Format.Money(d.DimSessionUsd, "USD", estimate: true, icons.Set)} session", Palette.Label);
    }

    /// <summary>§6.1 #4 -- reverse video on critical (203); the countdown reuses the same reset the binding window's bar shows.</summary>
    private static void RenderBlocked(AnsiBuilder b, SpendSlotData d, IconGlyphs icons)
    {
        b.ReverseBanner($"{icons.Blocked} LIMIT REACHED", Palette.Critical).Raw(" ");
        b.Colored("resets ", Palette.Label);
        var countdown = Format.Countdown(d.Binding?.Remaining ?? TimeSpan.Zero);
        if (countdown.Length > 0) b.Colored(countdown, Palette.Critical, bold: true);
    }

    /// <summary>§6.1 #2 -- reverse video on session-share ember (202); real figures only, never "&#8776;".</summary>
    private static void RenderCredits(AnsiBuilder b, SpendSlotData d, IconGlyphs icons, bool compact)
    {
        b.ReverseBanner($"{icons.Credits} CREDITS", Palette.SessionShare).Raw(" ");

        var sessionUsd = Format.MinorUnitsToMajor(d.CreditsSessionMinor ?? 0, d.CreditsCurrency);
        var todayUsd = Format.MinorUnitsToMajor(d.CreditsTodayMinor ?? 0, d.CreditsCurrency);
        var session = Format.Money(sessionUsd, d.CreditsCurrency, estimate: false, icons.Set);
        var today = Format.Money(todayUsd, d.CreditsCurrency, estimate: false, icons.Set);
        var dot = Format.Dot(icons.Set);

        b.Colored(session, Palette.GradientStop1, bold: true);
        b.Colored(compact ? $" {dot} " : $" session {dot} ", Palette.Label);
        b.Colored(today, Palette.GradientStop1, bold: true);
        if (!compact) b.Colored(" today", Palette.Label);
    }
}
