using System.Text.Json.Serialization;

namespace Ember.Core.Data;

/// <summary>The stdin data contract, §3. Every object is permissive: unknown fields are ignored.</summary>
public sealed class StdinPayload
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("model")]
    public ModelInfo? Model { get; init; }

    [JsonPropertyName("effort")]
    public EffortInfo? Effort { get; init; }

    [JsonPropertyName("workspace")]
    public WorkspaceInfo? Workspace { get; init; }

    [JsonPropertyName("cost")]
    public CostInfo? Cost { get; init; }

    [JsonPropertyName("context_window")]
    public ContextWindowInfo? ContextWindow { get; init; }

    [JsonPropertyName("rate_limits")]
    public RateLimitsInfo? RateLimits { get; init; }
}

public sealed class ModelInfo
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }
}

/// <summary>Absent when the current model has no reasoning-effort parameter (§3.1).</summary>
public sealed class EffortInfo
{
    [JsonPropertyName("level")]
    public string? Level { get; init; } // low | medium | high | xhigh | max
}

public sealed class WorkspaceInfo
{
    [JsonPropertyName("project_dir")]
    public string? ProjectDir { get; init; }

    /// <summary>Absent outside a git repo or with no origin remote (§3.1); preferred over <see cref="ProjectDir"/> when present.</summary>
    [JsonPropertyName("repo")]
    public RepoInfo? Repo { get; init; }

    [JsonPropertyName("current_dir")]
    public string? CurrentDir { get; init; }
}

public sealed class RepoInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class CostInfo
{
    [JsonPropertyName("total_duration_ms")]
    public long TotalDurationMs { get; init; }

    [JsonPropertyName("total_cost_usd")]
    public double TotalCostUsd { get; init; }
}

/// <summary>
/// <see cref="UsedPercentage"/> may be null early in a session or right after
/// <c>/compact</c>, until the next model call repopulates it (§3.1).
/// </summary>
public sealed class ContextWindowInfo
{
    [JsonPropertyName("used_percentage")]
    public double? UsedPercentage { get; init; }
}

/// <summary>
/// Present only for Claude.ai Pro/Max subscribers or behind a Claude apps
/// gateway, and only after the session's first API response (§3.1). Each
/// window may be independently absent.
/// </summary>
public sealed class RateLimitsInfo
{
    [JsonPropertyName("five_hour")]
    public RateLimitWindow? FiveHour { get; init; }

    [JsonPropertyName("seven_day")]
    public RateLimitWindow? SevenDay { get; init; }
}

public sealed class RateLimitWindow
{
    [JsonPropertyName("used_percentage")]
    public double UsedPercentage { get; init; }

    /// <summary>Unix epoch seconds -- unlike the usage API's ISO 8601 strings (§4.2).</summary>
    [JsonPropertyName("resets_at")]
    public long ResetsAt { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StdinPayload))]
public partial class StdinJsonContext : JsonSerializerContext
{
}
