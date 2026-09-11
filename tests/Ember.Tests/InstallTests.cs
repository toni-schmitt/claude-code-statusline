using System.Text.Json.Nodes;
using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

[Collection("EnvironmentVariables")]
public class InstallTests : IDisposable
{
    private readonly string? _origConfigDir;
    private readonly string _tempConfigDir;
    private readonly string _settingsPath;

    public InstallTests()
    {
        _origConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        _tempConfigDir = Directory.CreateTempSubdirectory("ember-install-").FullName;
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _tempConfigDir);
        _settingsPath = Path.Combine(_tempConfigDir, "settings.json");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _origConfigDir);
        Directory.Delete(_tempConfigDir, recursive: true);
    }

    [Fact]
    public void InstallPreservesExistingKeys()
    {
        File.WriteAllText(_settingsPath, """{"myCustomKey":"keepMe","allowedTools":["bash"]}""");

        Ember.Program.Main(["--install"]);

        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!.AsObject();
        Assert.Equal("keepMe", root["myCustomKey"]?.GetValue<string>());
        Assert.NotNull(root["statusLine"]);
        Assert.NotNull(root["subagentStatusLine"]);
    }

    [Fact]
    public void InstallRefusesToOverwriteCorruptSettings()
    {
        File.WriteAllText(_settingsPath, "{ this is not valid json at all");
        var originalContent = File.ReadAllText(_settingsPath);

        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            Ember.Program.Main(["--install"]);
        }
        finally
        {
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }

        Assert.Equal(originalContent, File.ReadAllText(_settingsPath));
        Assert.Contains("not valid JSON", stderr.ToString());
    }

    [Fact]
    public void InstallHandlesJsoncWithCommentsAndTrailingCommas()
    {
        File.WriteAllText(_settingsPath, """
        {
            // this is a comment
            "myKey": "myValue",
            "list": [1, 2, 3,],
        }
        """);

        Ember.Program.Main(["--install"]);

        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!.AsObject();
        Assert.Equal("myValue", root["myKey"]?.GetValue<string>());
        Assert.NotNull(root["statusLine"]);
    }
}
