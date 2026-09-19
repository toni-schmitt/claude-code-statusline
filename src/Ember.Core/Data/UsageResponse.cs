using System.Text.Json;
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
    [JsonConverter(typeof(MinorUnitsJsonConverter))]
    public long? MonthlyLimit { get; init; }

    /// <summary>Minor units, month-to-date (§4.1, §7.2).</summary>
    [JsonPropertyName("used_credits")]
    [JsonConverter(typeof(MinorUnitsJsonConverter))]
    public long? UsedCredits { get; init; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("disabled_reason")]
    public string? DisabledReason { get; init; }
}

/// <summary>
/// A minor-unit amount, accepted whether the endpoint spells it as an integer
/// or as a fractional number — <c>extra_usage.used_credits</c> arrives as
/// <c>4849.0</c>, not <c>4849</c>.
/// <para>
/// Minor units are whole by definition (§4.1), so the fraction is an artifact
/// of the server encoding the figure as a float; it is rounded away rather
/// than widening §7.2's session and daily deltas, which are exact integer
/// arithmetic over watermarks and must not drift.
/// </para>
/// <para>
/// Without this the strict <c>long</c> read throws, and one unexpected token
/// fails the entire response. §10 rule 5 keeps nothing from a failed fetch, so
/// the line would hold its last good values — which is the right behaviour for
/// a transient error, and indistinguishable from a permanent one: every credit
/// figure freezes, and the §6 slot falls back to a credit status from whenever
/// the response last parsed.
/// </para>
/// </summary>
public sealed class MinorUnitsJsonConverter : JsonConverter<long?>
{
    /// <inheritdoc/>
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null) return null;
        if (reader.TokenType is not JsonTokenType.Number)
            throw new JsonException($"expected a number for a minor-unit amount, got {reader.TokenType}");

        if (reader.TryGetInt64(out var exact)) return exact;

        var approximate = Math.Round(reader.GetDouble(), MidpointRounding.AwayFromZero);

        // Out of range is not a figure anyone can spend; null reads as "no
        // figure", which §6 already treats as unknown rather than as zero.
        if (double.IsFinite(approximate) is false) return null;
        if (approximate is < long.MinValue or > long.MaxValue) return null;

        return (long)approximate;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
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
