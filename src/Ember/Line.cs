using Ember.Core;
using Ember.Core.Data;
using Ember.Core.Render;

namespace Ember;

public sealed record Line1Input(
    IconGlyphs Icons,
    string? ModelDisplayName,
    string? EffortLevel,
    string ProjectName,
    string? Branch,
    TimeSpan SessionDuration,
    double? ContextUsedPercentage,
    long? ContextUsedTokens = null);

public sealed record Line2Input(
    IconGlyphs Icons,
    RateLimitWindow? FiveHour,
    RateLimitWindow? SevenDay,
    IReadOnlyList<ModelWindow> ModelWindows,
    double? FiveHourShare,
    DateTimeOffset Now,
    SpendSlotKind SpendKind,
    SpendSlotData SpendData);

/// <summary>
/// §2's line composition plus §13's width degradation. Per §13.3, the
/// ladder is not a table of width constants: each tier is fully composed
/// and measured, and the next tier is tried only if the current one
/// overflows. That is what lets the same tier list serve both the
/// ~99-column dim-estimate states and the 107-column credits banner (§13.3)
/// without a second table.
/// </summary>
public static class Line
{
    private readonly record struct Line1Tier(bool ShowCtx, bool ShowDuration, bool ShowEffort);

    // §13.2, in shed order: ctx -> duration -> effort. Project and branch
    // never appear here -- they never shed on width, only on data absence.
    private static readonly Line1Tier[] Line1Tiers =
    [
        new(true, true, true),
        new(false, true, true),
        new(false, false, true),
        new(false, false, false),
    ];

    public static string ComposeLine1(Line1Input input, int columns)
    {
        var chosen = ComposeLine1Tier(input, Line1Tiers[0]);
        if (chosen.Width > columns)
        {
            for (int i = 1; i < Line1Tiers.Length; i++)
            {
                chosen = ComposeLine1Tier(input, Line1Tiers[i]);
                if (chosen.Width <= columns) break;
            }
        }
        return chosen.Build();
    }

    private static AnsiBuilder ComposeLine1Tier(Line1Input input, Line1Tier tier)
    {
        var b = new AnsiBuilder();
        bool wrote = false;
        void Sep()
        {
            if (wrote) b.Colored($" {input.Icons.Separator} ", Palette.Separator);
            wrote = true;
        }

        // model [+ effort] — skipped entirely when the model name is absent
        if (input.ModelDisplayName is { Length: > 0 } model)
        {
            b.Colored($"{input.Icons.Model} ", Palette.Label);
            b.Gradient(model, Gradient.ForText(model));
            if (tier.ShowEffort && input.EffortLevel is { Length: > 0 } effort)
            {
                b.Colored($" {input.Icons.Effort} ", Palette.Label);
                b.Colored(effort, Palette.GradientStop6);
            }
            wrote = true;
        }

        // project [+ branch]
        Sep();
        b.Colored($"{input.Icons.Project} ", Palette.Label);
        b.Colored(input.ProjectName, Palette.GradientStop2);
        if (input.Branch is { Length: > 0 } branch)
        {
            b.Colored($" {input.Icons.Branch} ", Palette.Label);
            b.Colored(branch, Palette.Branch);
        }

        // duration
        if (tier.ShowDuration)
        {
            Sep();
            b.Colored($"{input.Icons.Clock} ", Palette.Label);
            b.Colored(Format.Duration(input.SessionDuration), Palette.GradientStop2);
        }

        // ctx -- absence (null used_percentage, §3.1) hides the segment same as a width shed would
        if (tier.ShowCtx && input.ContextUsedPercentage is double ctx)
        {
            Sep();
            b.Colored("ctx ", Palette.Label);
            b.Colored(Format.Percent(ctx), Palette.GradientStop2);
            if (input.ContextUsedTokens is long tokens)
            {
                b.Colored($" ({Format.TokenCount(tokens)})", Palette.Label);
            }
        }

        return b;
    }

    private enum SpendVisibility { Full, Compact, Hidden }

    private readonly record struct Line2Tier(
        bool ModelCountdown, bool SevenDayCountdown, int Cells, SpendVisibility Spend, bool ShowShare, bool ShowModels);

    // §13.1, in shed order: per-model countdowns -> 7d countdown -> bars halve
    // -> per-model windows -> spend slot loses its words -> spend slot drops
    // -> share segment drops.
    //
    // The per-model tiers are additive: with no §5.7 windows to render, tier 0
    // measures the same as tier 1 and tier 3 the same as tier 4, so an account
    // without them sheds exactly as it did before they existed.
    private static readonly Line2Tier[] Line2Tiers =
    [
        new(true, true, 10, SpendVisibility.Full, true, true),
        new(false, true, 10, SpendVisibility.Full, true, true),
        new(false, false, 10, SpendVisibility.Full, true, true),
        new(false, false, 5, SpendVisibility.Full, true, true),
        new(false, false, 5, SpendVisibility.Full, true, false),
        new(false, false, 5, SpendVisibility.Compact, true, false),
        new(false, false, 5, SpendVisibility.Hidden, true, false),
        new(false, false, 5, SpendVisibility.Hidden, false, false),
    ];

    public static string ComposeLine2(Line2Input input, int columns)
    {
        var chosen = ComposeLine2Tier(input, Line2Tiers[0]);
        if (chosen.Width > columns)
        {
            for (int i = 1; i < Line2Tiers.Length; i++)
            {
                chosen = ComposeLine2Tier(input, Line2Tiers[i]);
                if (chosen.Width <= columns) break;
            }
        }
        return chosen.Build();
    }

    private static AnsiBuilder ComposeLine2Tier(Line2Input input, Line2Tier tier)
    {
        var b = new AnsiBuilder();
        bool wrote = false;
        void Sep()
        {
            if (wrote) b.Colored($" {input.Icons.Separator} ", Palette.Separator);
            wrote = true;
        }

        if (input.FiveHour is { } fh)
        {
            Sep();
            RenderWindow(b, "5h", fh.UsedPercentage, DateTimeOffset.FromUnixTimeSeconds(fh.ResetsAt),
                tier.Cells, input.Icons, showCountdown: true, input.Now);
        }
        if (input.SevenDay is { } sd)
        {
            Sep();
            RenderWindow(b, "7d", sd.UsedPercentage, DateTimeOffset.FromUnixTimeSeconds(sd.ResetsAt),
                tier.Cells, input.Icons, showCountdown: tier.SevenDayCountdown, input.Now);
        }
        if (tier.ShowModels)
        {
            foreach (var model in input.ModelWindows)
            {
                Sep();
                RenderWindow(b, model.Label, model.UsedPercentage, model.ResetsAt,
                    tier.Cells, input.Icons, showCountdown: tier.ModelCountdown, input.Now);
            }
        }
        if (input.FiveHour is not null && tier.ShowShare)
        {
            Sep();
            b.Colored($"{input.Icons.Share} ", Palette.Label);
            b.Colored($"+{Format.Percent(input.FiveHourShare ?? 0)}", Palette.SessionShare);
            b.Colored(" of 5h", Palette.Label);
        }
        if (tier.Spend != SpendVisibility.Hidden)
        {
            Sep();
            SpendSlot.Render(b, input.SpendKind, input.SpendData, input.Icons, compact: tier.Spend == SpendVisibility.Compact);
        }

        return b;
    }

    /// <summary>
    /// One labelled bar, percentage and countdown. Takes the figures rather
    /// than a window type so stdin's epoch-second windows (§3) and the API's
    /// per-model ones (§5.7) render through one grammar, as §2.2 requires.
    /// </summary>
    /// <param name="resetsAt">Null when the source reported no reset; the countdown is then omitted.</param>
    private static void RenderWindow(
        AnsiBuilder b, string label, double usedPercentage, DateTimeOffset? resetsAt,
        int cells, IconGlyphs icons, bool showCountdown, DateTimeOffset now)
    {
        b.Colored($"{label} ", Palette.Label);
        b.Bar(Meter.Render(usedPercentage, cells, icons.Bar));
        b.Raw(" ");
        var (color, bold) = Palette.Severity(usedPercentage);
        b.Colored(Format.Percent(usedPercentage), color, bold);

        if (showCountdown is false) return;
        if (resetsAt is not DateTimeOffset reset) return;

        var countdown = Format.Countdown(reset - now);
        if (countdown.Length > 0) b.Colored($" {icons.Reset} {countdown}", Palette.Label);
    }
}
