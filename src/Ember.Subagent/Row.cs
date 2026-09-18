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
    public static string Compose(SubagentTask task, IconGlyphs icons, IconSet iconSet, int columns)
    {
        var b = new AnsiBuilder();
        bool active = task.Status is "running" or "failed"; // §11.3: queued/done render flat 240
        int labelColor = active ? Palette.Label : Palette.UnfilledBar;
        int sepColor = active ? Palette.Separator : Palette.UnfilledBar;

        var (marker, markerColor) = MarkerFor(task.Status, iconSet, icons);
        b.Colored($"{marker} ", markerColor);

        var name = task.Name ?? task.Type ?? "";
        if (active) b.Gradient(name, Gradient.ForText(name)); else b.Colored(name, Palette.UnfilledBar);

        if (task.Model is { Length: > 0 } modelId)
        {
            b.Colored($" {icons.Model} ", labelColor);
            b.Colored(ModelNames.Resolve(modelId), active ? Palette.GradientStop2 : Palette.UnfilledBar);

            var effortText = DescribeEffort(task.Effort);
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
        var bar = Meter.Render(pct, 10, icons.Bar);
        if (!active) bar = Flatten(bar); // shape stays legible; severity colour never does, on a row nothing is happening in
        b.Bar(bar);
        var (pctColor, pctBold) = Palette.Severity(pct);
        b.Colored($" {Format.Percent(pct)}", active ? pctColor : Palette.UnfilledBar, active && pctBold);
        b.Colored($" {Format.TokenCount(task.TokenCount ?? 0)}", labelColor);

        b.Colored($" {icons.Separator} ", sepColor);
        int used = b.Width;
        var desc = task.Description ?? "";
        int room = Math.Max(0, columns - used);
        int truncLen = AnsiBuilder.TruncateToWidth(desc, room);
        b.Colored(desc[..truncLen], Palette.UnfilledBar);

        return b.Build();
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

    private static (string Glyph, int Color) MarkerFor(string? status, IconSet set, IconGlyphs icons) => status switch
    {
        "running" => (set == IconSet.Ascii ? "*" : "●", Palette.Branch),
        "failed" => (set == IconSet.Ascii ? "*" : "●", Palette.Critical),
        "done" => (icons.Done, Palette.UnfilledBar),
        _ => (set == IconSet.Ascii ? "." : "○", Palette.UnfilledBar), // queued / unrecognised
    };

    private static string? DescribeEffort(JsonElement effort) => effort.ValueKind switch
    {
        JsonValueKind.String => effort.GetString(),
        JsonValueKind.Number => Format.TokenCount(effort.GetInt64()),
        _ => null, // absent -> inherited (§11.1)
    };
}
