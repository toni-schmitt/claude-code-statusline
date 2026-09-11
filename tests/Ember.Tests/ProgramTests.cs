using Xunit;
using static Ember.Tests.TestSupport;

namespace Ember.Tests;

[Collection("EnvironmentVariables")]
public class ProgramTests : IDisposable
{
    private readonly string? _origConfigDir;
    private readonly string? _origTmpDir;
    private readonly string? _origColumns;
    private readonly TextReader _origIn;
    private readonly TextWriter _origOut;
    private readonly string _tempConfigDir;
    private readonly string _tempTmpDir;

    public ProgramTests()
    {
        _origConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        _origTmpDir = Environment.GetEnvironmentVariable("TMPDIR");
        _origColumns = Environment.GetEnvironmentVariable("COLUMNS");
        _origIn = Console.In;
        _origOut = Console.Out;

        _tempConfigDir = Directory.CreateTempSubdirectory("ember-program-config-").FullName;
        _tempTmpDir = Directory.CreateTempSubdirectory("ember-program-tmp-").FullName;

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _tempConfigDir);
        Environment.SetEnvironmentVariable("TMPDIR", _tempTmpDir);
        Environment.SetEnvironmentVariable("COLUMNS", "120");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _origConfigDir);
        Environment.SetEnvironmentVariable("TMPDIR", _origTmpDir);
        Environment.SetEnvironmentVariable("COLUMNS", _origColumns);
        Console.SetIn(_origIn);
        Console.SetOut(_origOut);
        try { Directory.Delete(_tempConfigDir, recursive: true); } catch { }
        try { Directory.Delete(_tempTmpDir, recursive: true); } catch { }
    }

    [Fact]
    public void MainOutputsTwoNonEmptyLinesForValidPayload()
    {
        var payload = """
        {
            "session_id": "test-session",
            "model": {"display_name": "Opus 5"},
            "effort": {"level": "high"},
            "workspace": {"project_dir": "/tmp/my-project"},
            "cost": {"total_duration_ms": 60000, "total_cost_usd": 0.42},
            "context_window": {"used_percentage": 15.0},
            "rate_limits": {
                "five_hour": {"used_percentage": 30, "resets_at": 1000},
                "seven_day": {"used_percentage": 10, "resets_at": 500000}
            }
        }
        """;
        Console.SetIn(new StringReader(payload));
        var output = new StringWriter();
        Console.SetOut(output);

        int exitCode = Ember.Program.Main([]);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        var lines = text.Split('\n');
        Assert.True(lines.Length >= 2, $"Expected at least 2 lines, got {lines.Length}");

        var line1 = StripAnsi(lines[0]);
        var line2 = StripAnsi(lines[1]);
        Assert.Contains("Opus 5", line1);
        Assert.Contains("my-project", line1);
        Assert.Contains("5h", line2);
    }

    [Fact]
    public void MainOutputsTwoLinesEvenWithEmptyStdin()
    {
        Console.SetIn(new StringReader(""));
        var output = new StringWriter();
        Console.SetOut(output);

        int exitCode = Ember.Program.Main([]);

        Assert.Equal(0, exitCode);
        var lines = output.ToString().Split('\n');
        Assert.True(lines.Length >= 2);
    }

    [Fact]
    public void MainOutputsTwoLinesForCorruptStdin()
    {
        Console.SetIn(new StringReader("not json at all"));
        var output = new StringWriter();
        Console.SetOut(output);

        int exitCode = Ember.Program.Main([]);

        Assert.Equal(0, exitCode);
        var lines = output.ToString().Split('\n');
        Assert.True(lines.Length >= 2);
    }
}
