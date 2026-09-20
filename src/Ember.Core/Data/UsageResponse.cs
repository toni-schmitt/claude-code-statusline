using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core.Data;

/// <summary>
/// The undocumented usage API contract, §4.1. Transcribed from the response
/// validator inside the Claude Code binary. Every object is permissive:
/// unknown fields are ignored rather than rejected.
/// <para>
/// For every window that has a named top-level object, <c>limits[]</c> only
/// repeats as <c>percent</c> what that object already carries as
/// <c>utilization</c>. It is read for the one thing it carries alone: §5.7's
/// per-model weekly windows, which have no top-level object of their own.
/// </para>
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

    /// <summary>Per-scope breakdowns. Only the §5.7 rows are read; see <see cref="UsageLimit"/>.</summary>
    [JsonPropertyName("limits")]
    public List<UsageLimit>? Limits { get; init; }
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

/// <summary>
/// One <c>limits[]</c> row. Only <c>weekly_scoped</c> rows are read, for
/// §5.7's per-model weekly windows; every other <c>kind</c> repeats a window
/// that already has a named top-level object.
/// </summary>
public sealed class UsageLimit
{
    /// <summary>Which window the row describes. Observed: <c>session</c>, <c>weekly_all</c>, <c>weekly_scoped</c>.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    /// <summary>Utilisation 0-100 -- the same scale as <see cref="UsageWindow.Utilization"/>, and may exceed 100.</summary>
    [JsonPropertyName("percent")]
    public double? Percent { get; init; }

    /// <summary>When the window rolls over, or null when the endpoint reports no reset for it.</summary>
    [JsonPropertyName("resets_at")]
    [JsonConverter(typeof(ResetsAtJsonConverter))]
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>What the row is narrowed to; null on the plan-wide rows. Only <c>model</c> is read, never <c>surface</c>.</summary>
    [JsonPropertyName("scope")]
    public UsageLimitScope? Scope { get; init; }
}

/// <summary>The narrowing on a <see cref="UsageLimit"/>.</summary>
public sealed class UsageLimitScope
{
    /// <summary>The model bucket, present on <c>weekly_scoped</c> rows.</summary>
    [JsonPropertyName("model")]
    public UsageLimitModel? Model { get; init; }
}

/// <summary>The model bucket a <c>weekly_scoped</c> row belongs to.</summary>
public sealed class UsageLimitModel
{
    /// <summary>Server-supplied label, e.g. <c>Fable</c>. The row's only usable identifier -- its sibling <c>id</c> arrives null.</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }
}

/// <summary>
/// A <c>limits[]</c> reset instant, accepted whether the endpoint spells it
/// as an ISO 8601 string or as epoch seconds.
/// <para>
/// ISO 8601 is what the endpoint is observed to send, but Claude Code's own
/// projection of these rows branches on <c>typeof resets_at === "number"</c>,
/// so both spellings reach a reader in practice. A strict string read throws
/// on the numeric one, and §10 rule 5 keeps nothing from a failed response --
/// so one unexpected token here would cost the credit figures too, not just
/// the window it appeared in.
/// </para>
/// <para>
/// An unparseable or out-of-range value reads as null, which §5.7 already
/// treats as "no reset to count down to" rather than as an error.
/// </para>
/// </summary>
public sealed class ResetsAtJsonConverter : JsonConverter<DateTimeOffset?>
{
    /// <inheritdoc/>
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null) return null;

        if (reader.TokenType is JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var epochSeconds) is false) return null;
            if (epochSeconds < DateTimeOffset.MinValue.ToUnixTimeSeconds()) return null;
            if (epochSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds()) return null;
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        }

        if (reader.TokenType is not JsonTokenType.String)
            throw new JsonException($"expected a string or a number for a reset instant, got {reader.TokenType}");

        return DateTimeOffset.TryParse(
            reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteStringValue(value.Value);
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
