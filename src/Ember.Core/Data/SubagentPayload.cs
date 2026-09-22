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
    public string? Status { get; init; } // pending | running | completed | failed | killed | paused, as Claude Code sends them; §11.1 spells the finished state "done"

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
