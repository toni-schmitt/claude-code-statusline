using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core.Data;

/// <summary>
/// §11.1's input contract. Only <c>columns</c> and <c>tasks[]</c> are
/// modelled -- the base hook envelope fields aren't consumed by row
/// rendering, matching §3.2's "fields deliberately not consumed" precedent.
/// Unknown JSON members are dropped automatically.
/// </summary>
public sealed class SubagentStdinPayload
{
    [JsonPropertyName("columns")]
    public int? Columns { get; init; }

    [JsonPropertyName("tasks")]
    public List<SubagentTask>? Tasks { get; init; }
}

/// <summary>
/// <c>label</c>, <c>startTime</c>, <c>cwd</c> and
/// <c>tokenSamples</c> are part of §11.1's contract but aren't consumed by
/// this design, so they aren't modelled here either.
/// </summary>
public sealed class SubagentTask
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; } // "local_agent" for every Agent-tool spawn; the headline of last resort when neither name nor description is present

    [JsonPropertyName("status")]
    [JsonConverter(typeof(SubagentStatusJsonConverter))]
    public SubagentStatus Status { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Resolved model ID (§11.1); omitted while the model is unresolved.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Either a level string or a numeric token budget (§11.1); default (Undefined) when absent, meaning the subagent inherits the session's effort.</summary>
    [JsonPropertyName("effort")]
    public JsonElement Effort { get; init; }

    [JsonPropertyName("contextWindowSize")]
    public long? ContextWindowSize { get; init; }

    [JsonPropertyName("tokenCount")]
    public long? TokenCount { get; init; }
}

/// <summary>
/// A task's lifecycle state. Claude Code sends <c>pending</c>, <c>running</c>,
/// <c>completed</c>, <c>failed</c>, <c>killed</c> and <c>paused</c>; §11.1
/// spells the first and third <c>queued</c> and <c>done</c>, and both spellings
/// are accepted so the spec's examples keep rendering.
/// </summary>
public enum SubagentStatus
{
    Unknown,
    Pending,
    Running,
    Completed,
    Failed,
    Killed,
    Paused,
}

/// <summary>
/// Maps the status string onto <see cref="SubagentStatus"/> at the JSON
/// boundary, so the renderer never matches on spellings. Anything
/// unrecognised, including a missing or non-string value, becomes
/// <see cref="SubagentStatus.Unknown"/> rather than failing the payload: one
/// odd task must not blank every row.
/// </summary>
public sealed class SubagentStatusJsonConverter : JsonConverter<SubagentStatus>
{
    public override bool HandleNull => true;

    /// <inheritdoc/>
    public override SubagentStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String) return SubagentStatus.Unknown;
        return Parse(reader.GetString());
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SubagentStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
    }

    public static SubagentStatus Parse(string? status) => status?.ToLowerInvariant() switch
    {
        "pending" or "queued" => SubagentStatus.Pending,
        "running" => SubagentStatus.Running,
        "completed" or "done" => SubagentStatus.Completed,
        "failed" => SubagentStatus.Failed,
        "killed" => SubagentStatus.Killed,
        "paused" => SubagentStatus.Paused,
        _ => SubagentStatus.Unknown,
    };
}

/// <summary>§11.2's output contract: one line per row to override.</summary>
public sealed class SubagentRowOutput
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SubagentStdinPayload))]
[JsonSerializable(typeof(SubagentRowOutput))]
public partial class SubagentJsonContext : JsonSerializerContext
{
}
