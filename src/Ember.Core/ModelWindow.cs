using Ember.Core.Data;

namespace Ember.Core;

/// <summary>
/// A weekly rate-limit window scoped to a single model bucket, §5.7 -- the
/// dedicated Fable allowance a Max plan carries alongside its plan-wide
/// weekly window, and any sibling bucket the endpoint grows later.
/// </summary>
/// <param name="Label">
/// The bucket's name as line 2 prints it: the server's <c>display_name</c>
/// lower-cased, so it sits beside <c>5h</c> and <c>7d</c> rather than
/// shouting over them.
/// </param>
/// <param name="UsedPercentage">Utilisation 0-100. May exceed 100, which §5.4's bar clamps while the label still prints the true figure.</param>
/// <param name="ResetsAt">When the window rolls over, or null when the endpoint reported none -- the countdown is then omitted rather than guessed.</param>
public sealed record ModelWindow(string Label, double UsedPercentage, DateTimeOffset? ResetsAt);

/// <summary>
/// Projects §4.1's <c>limits[]</c> into the per-model windows line 2 renders.
/// <para>
/// These are API-sourced, so §9.3 binds: the segments are additive and vanish
/// whole when the refresher has nothing. The stdin-sourced <c>7d</c> bar
/// remains the weekly figure the line actually stands behind.
/// </para>
/// </summary>
public static class ModelWindows
{
    /// <summary>The <c>kind</c> marking a row as scoped to one model rather than to the whole plan.</summary>
    private const string WeeklyScoped = "weekly_scoped";

    /// <summary>
    /// A bucket nobody has touched is not news, and on a plan carrying several
    /// of them the untouched ones would spend line 2's width on a row of
    /// zeroes all week. Half a percent rather than zero because §5.6 rounds
    /// the label half-up: below it the segment reads "0%" against an empty
    /// bar, which is the thing being suppressed.
    /// </summary>
    private const double MinimumRenderedPercentage = 0.5;

    /// <summary>
    /// The renderable per-model windows, in payload order. Empty when there is
    /// no usage response, no <c>limits[]</c>, or nothing in it clearing
    /// <see cref="MinimumRenderedPercentage"/>.
    /// </summary>
    /// <param name="usage">The refresher's cached §4.1 response, or null while the cache is cold.</param>
    /// <returns>One <see cref="ModelWindow"/> per qualifying <c>weekly_scoped</c> row.</returns>
    public static IReadOnlyList<ModelWindow> From(UsageResponse? usage)
    {
        if (usage?.Limits is not { Count: > 0 } limits) return [];

        var windows = new List<ModelWindow>();
        foreach (var limit in limits)
        {
            if (limit.Kind is not WeeklyScoped) continue;
            if (limit.Scope?.Model?.DisplayName is not { Length: > 0 } displayName) continue;
            if (limit.Percent is not double percent) continue;
            if (percent < MinimumRenderedPercentage) continue;

            windows.Add(new ModelWindow(displayName.ToLowerInvariant(), percent, limit.ResetsAt));
        }

        return windows;
    }
}
