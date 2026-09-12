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
                b.Colored($" {input.Icons.Effort}", Palette.Label);
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

    private readonly record struct Line2Tier(bool SevenDayCountdown, int Cells, SpendVisibility Spend, bool ShowShare);

    // §13.1, in shed order: 7d countdown -> bars halve to 5 cells -> spend
    // slot loses its words -> spend slot drops -> share segment drops.
    private static readonly Line2Tier[] Line2Tiers =
    [
        new(true, 10, SpendVisibility.Full, true),
        new(false, 10, SpendVisibility.Full, true),
        new(false, 5, SpendVisibility.Full, true),
        new(false, 5, SpendVisibility.Compact, true),
        new(false, 5, SpendVisibility.Hidden, true),
        new(false, 5, SpendVisibility.Hidden, false),
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
            RenderWindow(b, "5h", fh, tier.Cells, input.Icons, showCountdown: true, input.Now);
        }
        if (input.SevenDay is { } sd)
        {
            Sep();
            RenderWindow(b, "7d", sd, tier.Cells, input.Icons, showCountdown: tier.SevenDayCountdown, input.Now);
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

    private static void RenderWindow(
        AnsiBuilder b, string label, RateLimitWindow w, int cells,
        IconGlyphs icons, bool showCountdown, DateTimeOffset now)
    {
        b.Colored($"{label} ", Palette.Label);
        b.Bar(Meter.Render(w.UsedPercentage, cells, icons.Bar));
        b.Raw(" ");
        var (color, bold) = Palette.Severity(w.UsedPercentage);
        b.Colored(Format.Percent(w.UsedPercentage), color, bold);

        if (showCountdown)
        {
            var countdown = Format.Countdown(DateTimeOffset.FromUnixTimeSeconds(w.ResetsAt) - now);
            if (countdown.Length > 0) b.Colored($" {icons.Reset} {countdown}", Palette.Label);
        }
    }
}
