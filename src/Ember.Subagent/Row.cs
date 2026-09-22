using System.Text.Json;
using Ember.Core.Data;
using Ember.Core.Render;

namespace Ember.Subagent;

/// <summary>
/// §11.3's row composition: the status line's grammar on a narrower row.
/// The gradient applies only to active (running/failed) rows; queued and
/// done rows render <b>entirely</b> in 240 -- every label, separator and bar
/// cell, not just the name -- so work that isn't happening fully recedes
/// (bar shape is kept, since that's information, but never in severity
/// colour: an inactive row must never be the brightest thing on screen).
/// </summary>
public static class Row
{
    private readonly record struct RowTier(bool ShowEffort, int Cells, bool ShowTokens, bool ShowModel);

    // §13's approach on a row, in shed order: bar halves to 5 cells -> effort
    // -> token count -> model. The bar goes first because its shape is coarse
    // information at either length; the model goes last because it is what a
    // row is most often read for. The headline is elastic, so a tier is kept
    // as long as it leaves the headline at least HeadlineFloor columns (or its
    // full width, when shorter); the next tier is tried only when it does not.
    private static readonly RowTier[] Tiers =
    [
        new(true, 10, true, true),
        new(true, 5, true, true),
        new(false, 5, true, true),
        new(false, 5, false, true),
        new(false, 5, false, false),
    ];

    // A whole short description such as "Probe model-opus-4-8" is what the
    // metrics give way for; cutting it to a prefix would leave sibling rows
    // indistinguishable.
    private const int HeadlineFloor = 20;

    // A named row's trailing description is dropped, separator and all, once
    // it would be cut to fewer columns than this: "/cod" identifies nothing
    // and the separator costs three columns of its own.
    private const int TailFloor = 8;

    public static string Compose(SubagentTask task, IconGlyphs icons, IconSet iconSet, int columns)
    {
        var b = new AnsiBuilder();
        // §11.3: queued/done render flat 240. A killed task ended as abnormally
        // as a failed one, so it keeps the failed row's full palette.
        bool active = task.Status is SubagentStatus.Running or SubagentStatus.Failed or SubagentStatus.Killed;
        int labelColor = active ? Palette.Label : Palette.UnfilledBar;
        int sepColor = active ? Palette.Separator : Palette.UnfilledBar;

        var (marker, markerColor) = MarkerFor(task.Status, iconSet, icons);
        b.Colored($"{marker} ", markerColor);

        // Only agents spawned with an explicit name carry `name`; a plain
        // Agent-tool spawn arrives as `type: "local_agent"`, which says
        // nothing about which spawn it is. Its description is the one stable
        // text that sets it apart (`label` is the live activity and changes
        // every tick), so it takes the name slot and is not repeated after
        // the metrics.
        var description = task.Description ?? "";
        string headline, tail;
        if (task.Name is { Length: > 0 } name)
        {
            headline = name;
            tail = description;
        }
        else
        {
            headline = description.Length > 0 ? description : task.Type ?? "";
            tail = "";
        }

        // The metrics are composed before the text that precedes them, so the
        // headline can be fitted to whatever width they leave over.
        int floor = Math.Min(AnsiBuilder.DisplayWidth(headline), HeadlineFloor);
        var metrics = ComposeMetrics(task, Tiers[0], icons, active, labelColor, sepColor);
        for (int i = 1; i < Tiers.Length && columns - b.Width - metrics.Width < floor; i++)
        {
            metrics = ComposeMetrics(task, Tiers[i], icons, active, labelColor, sepColor);
        }

        headline = Fit(headline, columns - b.Width - metrics.Width);
        if (active) b.Gradient(headline, Gradient.ForText(headline)); else b.Colored(headline, Palette.UnfilledBar);
        b.Append(metrics);

        // The separator is only worth its columns when enough text follows it.
        var separator = $" {icons.Separator} ";
        int tailRoom = columns - b.Width - AnsiBuilder.DisplayWidth(separator);
        if (tailRoom < Math.Min(AnsiBuilder.DisplayWidth(tail), TailFloor)) return b.Build();
        tail = Fit(tail, tailRoom);
        if (tail.Length is 0) return b.Build();

        b.Colored(separator, sepColor);
        b.Colored(tail, Palette.UnfilledBar);
        return b.Build();
    }

    /// <summary>The longest prefix of <paramref name="text"/> that fits in <paramref name="room"/> columns; empty when no room is left.</summary>
    private static string Fit(string text, int room) => text[..AnsiBuilder.TruncateToWidth(text, Math.Max(0, room))];

    /// <summary>The model, effort and context segments that follow the name, at one tier of the ladder.</summary>
    private static AnsiBuilder ComposeMetrics(SubagentTask task, RowTier tier, IconGlyphs icons, bool active, int labelColor, int sepColor)
    {
        var b = new AnsiBuilder();
        if (tier.ShowModel && task.Model is { Length: > 0 } modelId)
        {
            b.Colored($" {icons.Model} ", labelColor);
            b.Colored(ModelNames.Resolve(modelId), active ? Palette.GradientStop2 : Palette.UnfilledBar);

            var effortText = tier.ShowEffort ? DescribeEffort(task.Effort) : null;
            if (effortText is { Length: > 0 })
            {
                b.Colored($" {icons.Effort} ", labelColor);
                b.Colored(effortText, active ? Palette.GradientStop6 : Palette.UnfilledBar);
            }
        }

        b.Colored($" {icons.Separator} ", sepColor);
        b.Colored("ctx ", labelColor);

        double pct = task.ContextWindowSize is long size && size > 0 && task.TokenCount is long tok
            ? Math.Clamp(100.0 * tok / size, 0, 999)
            : 0;
        var bar = Meter.Render(pct, tier.Cells, icons.Bar);
        if (!active) bar = Flatten(bar); // shape stays legible; severity colour never does, on a row nothing is happening in
        b.Bar(bar);
        var (pctColor, pctBold) = Palette.Severity(pct);
        b.Colored($" {Format.Percent(pct)}", active ? pctColor : Palette.UnfilledBar, active && pctBold);
        if (tier.ShowTokens) b.Colored($" {Format.TokenCount(task.TokenCount ?? 0)}", labelColor);
        return b;
    }

    private static BarCell[] Flatten(BarCell[] cells)
    {
        var flat = new BarCell[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            flat[i] = cells[i] with { Color = Palette.UnfilledBar, Bold = false };
        }
        return flat;
    }

    private static (string Glyph, int Color) MarkerFor(SubagentStatus status, IconSet set, IconGlyphs icons) => status switch
    {
        SubagentStatus.Running => (set == IconSet.Ascii ? "*" : "●", Palette.Branch),
        SubagentStatus.Failed or SubagentStatus.Killed => (set == IconSet.Ascii ? "*" : "●", Palette.Critical),
        SubagentStatus.Completed => (icons.Done, Palette.UnfilledBar),
        _ => (set == IconSet.Ascii ? "." : "○", Palette.UnfilledBar), // pending / paused / unknown
    };

    private static string? DescribeEffort(JsonElement effort) => effort.ValueKind switch
    {
        JsonValueKind.String => effort.GetString(),
        JsonValueKind.Number when effort.TryGetInt64(out long budget) => Format.TokenCount(budget),
        _ => null, // absent -> inherited (§11.1); a non-integral budget is not a shape §11.1 defines, and a throw here would cost the whole row
    };
}
