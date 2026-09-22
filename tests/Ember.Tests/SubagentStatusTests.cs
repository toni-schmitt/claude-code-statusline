using System.Text.Json;
using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

/// <summary>The status string is normalised once, when the payload is read.</summary>
public class SubagentStatusTests
{
    [Theory]
    [InlineData("pending", SubagentStatus.Pending)]
    [InlineData("queued", SubagentStatus.Pending)]
    [InlineData("running", SubagentStatus.Running)]
    [InlineData("completed", SubagentStatus.Completed)]
    [InlineData("done", SubagentStatus.Completed)]
    [InlineData("failed", SubagentStatus.Failed)]
    [InlineData("killed", SubagentStatus.Killed)]
    [InlineData("paused", SubagentStatus.Paused)]
    [InlineData("RUNNING", SubagentStatus.Running)]
    [InlineData("cancelled", SubagentStatus.Unknown)]
    [InlineData("", SubagentStatus.Unknown)]
    [InlineData(null, SubagentStatus.Unknown)]
    public void ParsesEverySpellingClaudeCodeAndTheSpecUse(string? status, SubagentStatus expected)
    {
        Assert.Equal(expected, SubagentStatusJsonConverter.Parse(status));
    }

    [Theory]
    [InlineData("\"running\"", SubagentStatus.Running)]
    [InlineData("null", SubagentStatus.Unknown)]
    [InlineData("42", SubagentStatus.Unknown)]
    public void DeserialisesWithoutFailingThePayload(string statusJson, SubagentStatus expected)
    {
        var task = JsonSerializer.Deserialize(
            $$"""{"id":"t","status":{{statusJson}}}""", SubagentJsonContext.Default.SubagentTask)!;
        Assert.Equal(expected, task.Status);
    }

    [Fact]
    public void AbsentStatusReadsAsUnknown()
    {
        var task = JsonSerializer.Deserialize("""{"id":"t"}""", SubagentJsonContext.Default.SubagentTask)!;
        Assert.Equal(SubagentStatus.Unknown, task.Status);
    }
}
