using System.Text.Json.Serialization;

namespace Ember.Core.Data;

/// <summary>
/// The undocumented usage API contract, §4.1. Transcribed from the response
/// validator inside the Claude Code binary. Every object is permissive:
/// unknown fields (including the whole <c>limits[]</c> array) are ignored
/// rather than rejected -- <c>limits[]</c> duplicates the same numbers as
/// <c>percent</c> that the named top-level objects carry as
/// <c>utilization</c>, and nothing here consumes it (§4.1).
/// </summary>
public sealed class UsageResponse
{
    [JsonPropertyName("five_hour")]
    public UsageWindow? FiveHour { get; init; }

    [JsonPropertyName("seven_day")]
    public UsageWindow? SevenDay { get; init; }

    [JsonPropertyName("seven_day_oauth_apps")]
    public UsageWindow? SevenDayOauthApps { get; init; }

    [JsonPropertyName("seven_day_opus")]
    public UsageWindow? SevenDayOpus { get; init; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsageWindow? SevenDaySonnet { get; init; }

    [JsonPropertyName("cinder_cove")]
    public UsageWindow? CinderCove { get; init; }

    [JsonPropertyName("extra_usage")]
    public ExtraUsage? ExtraUsage { get; init; }
}

public sealed class UsageWindow
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; init; }

    /// <summary>ISO 8601 -- unlike stdin's epoch-second <c>resets_at</c> (§4.2).</summary>
    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; init; }
}

public sealed class ExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool? IsEnabled { get; init; }

    /// <summary>Minor units; null means unlimited (§4.1).</summary>
    [JsonPropertyName("monthly_limit")]
    public long? MonthlyLimit { get; init; }

    /// <summary>Minor units, month-to-date (§4.1, §7.2).</summary>
    [JsonPropertyName("used_credits")]
    public long? UsedCredits { get; init; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("disabled_reason")]
    public string? DisabledReason { get; init; }
}

/// <summary>Claude Code's own cached snapshot, §4.4: <c>~/.claude.json</c> -&gt; <c>cachedUsageUtilization</c>.</summary>
public sealed class CachedUsageSnapshot
{
    [JsonPropertyName("fetchedAtMs")]
    public long FetchedAtMs { get; init; }

    [JsonPropertyName("accountUuid")]
    public string? AccountUuid { get; init; }

    [JsonPropertyName("utilization")]
    public UsageResponse? Utilization { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UsageResponse))]
[JsonSerializable(typeof(CachedUsageSnapshot))]
public partial class UsageJsonContext : JsonSerializerContext
{
}
