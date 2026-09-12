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

    /// <summary>
    /// Builds the layout Homebrew actually creates:
    /// <c>&lt;prefix&gt;/Cellar/ember/&lt;version&gt;/bin/ember</c> with <c>&lt;prefix&gt;/bin/ember</c>
    /// symlinked at it. Returns (realPath, linkPath).
    /// </summary>
    private static (string Real, string Link) FakeCellar(string root, string version = "0.1.0", string name = "ember")
    {
        var cellarBin = Path.Combine(root, "Cellar", name, version, "bin");
        var prefixBin = Path.Combine(root, "bin");
        Directory.CreateDirectory(cellarBin);
        Directory.CreateDirectory(prefixBin);

        var real = Path.Combine(cellarBin, name);
        var link = Path.Combine(prefixBin, name);
        File.WriteAllText(real, "#!/bin/sh\n");
        File.CreateSymbolicLink(link, real);
        return (real, link);
    }

    [Fact]
    public void PreferStableBinPathMapsCellarPathToTheStableSymlink()
    {
        var root = Directory.CreateTempSubdirectory("ember-brew-").FullName;
        try
        {
            var (real, link) = FakeCellar(root);

            // The version-pinned Cellar path is what Environment.ProcessPath reports;
            // what gets written must be the symlink that survives `brew upgrade`.
            Assert.Equal(link, Ember.Program.PreferStableBinPath(real));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferStableBinPathLeavesNonCellarPathsAlone()
    {
        var root = Directory.CreateTempSubdirectory("ember-plain-").FullName;
        try
        {
            var exe = Path.Combine(root, "ember");
            File.WriteAllText(exe, "#!/bin/sh\n");

            Assert.Equal(exe, Ember.Program.PreferStableBinPath(exe));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferStableBinPathIgnoresALinkPointingAtADifferentBinary()
    {
        var root = Directory.CreateTempSubdirectory("ember-conflict-").FullName;
        try
        {
            var (real, link) = FakeCellar(root);

            // Another formula owns <prefix>/bin/ember. Writing that path would point
            // Claude Code at someone else's binary, so the Cellar path must win.
            File.Delete(link);
            var impostor = Path.Combine(root, "Cellar", "not-ember", "9.9.9", "bin", "ember");
            Directory.CreateDirectory(Path.GetDirectoryName(impostor)!);
            File.WriteAllText(impostor, "#!/bin/sh\n");
            File.CreateSymbolicLink(link, impostor);

            Assert.Equal(real, Ember.Program.PreferStableBinPath(real));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferStableBinPathKeepsCellarPathWhenNothingIsLinked()
    {
        var root = Directory.CreateTempSubdirectory("ember-unlinked-").FullName;
        try
        {
            var (real, link) = FakeCellar(root);
            File.Delete(link); // `brew unlink ember`

            Assert.Equal(real, Ember.Program.PreferStableBinPath(real));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstallWritesTheStablePathAndDerivesTheSubagentBesideIt()
    {
        var root = Directory.CreateTempSubdirectory("ember-install-brew-").FullName;
        try
        {
            var (real, link) = FakeCellar(root);
            // ember-subagent ships in the same formula, so it is linked alongside.
            var subagentReal = Path.Combine(Path.GetDirectoryName(real)!, "ember-subagent");
            File.WriteAllText(subagentReal, "#!/bin/sh\n");
            File.CreateSymbolicLink(Path.Combine(root, "bin", "ember-subagent"), subagentReal);

            var exePath = Ember.Program.PreferStableBinPath(real);
            var subagentPath = Path.Combine(Path.GetDirectoryName(exePath)!, "ember-subagent");

            Assert.Equal(link, exePath);
            Assert.Equal(Path.Combine(root, "bin", "ember-subagent"), subagentPath);
            Assert.DoesNotContain("Cellar", exePath);
            Assert.DoesNotContain("Cellar", subagentPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
