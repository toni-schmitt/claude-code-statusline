using System.Text.Json.Serialization;

namespace Ember.Core.Data;

/// <summary>
/// The slice of <c>~/.claude.json</c> this design reads: the account uuid
/// used to key the state file (§8), and the cached usage snapshot usable as
/// a cold-start seed before the refresher has ever run (§4.4).
/// </summary>
public sealed class ClaudeConfig
{
    [JsonPropertyName("oauthAccount")]
    public OauthAccountInfo? OauthAccount { get; init; }

    [JsonPropertyName("cachedUsageUtilization")]
    public CachedUsageSnapshot? CachedUsageUtilization { get; init; }
}

public sealed class OauthAccountInfo
{
    [JsonPropertyName("accountUuid")]
    public string? AccountUuid { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ClaudeConfig))]
public partial class ClaudeConfigJsonContext : JsonSerializerContext
{
}
