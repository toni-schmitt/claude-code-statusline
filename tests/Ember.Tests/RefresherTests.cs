using Ember.Core.Data;
using Xunit;

namespace Ember.Tests;

/// <summary>
/// §10's cache path, TTL, failure cooldown and the symlink-safety check.
/// Runs against isolated CLAUDE_CONFIG_DIR/TMPDIR values for the duration of
/// each test and restores the real environment afterwards -- never touch a
/// developer's real ~/.claude or /tmp state from a test run.
/// </summary>
[Collection("EnvironmentVariables")] // serialises against other tests that touch process-wide env vars
public class RefresherTests : IDisposable
{
    private readonly string? _origConfigDir;
    private readonly string? _origTmpDir;
    private readonly string _tempConfigDir;
    private readonly string _tempTmpDir;

    public RefresherTests()
    {
        _origConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        _origTmpDir = Environment.GetEnvironmentVariable("TMPDIR");

        _tempConfigDir = Directory.CreateTempSubdirectory("ember-refresher-config-").FullName;
        _tempTmpDir = Directory.CreateTempSubdirectory("ember-refresher-tmp-").FullName;

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _tempConfigDir);
        Environment.SetEnvironmentVariable("TMPDIR", _tempTmpDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _origConfigDir);
        Environment.SetEnvironmentVariable("TMPDIR", _origTmpDir);
        Directory.Delete(_tempConfigDir, recursive: true);
        Directory.Delete(_tempTmpDir, recursive: true);
    }

    private static void WriteCacheSecure(string path, string content)
    {
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void CachePathIsDeterministicForTheSameConfigDir()
    {
        Assert.Equal(Refresher.CachePath(), Refresher.CachePath());
    }

    [Fact]
    public void CachePathLivesUnderTmpDir()
    {
        Assert.StartsWith(_tempTmpDir, Refresher.CachePath());
    }

    [Fact]
    public void MissingCacheIsStale()
    {
        Assert.True(Refresher.IsStale());
    }

    [Fact]
    public void FreshCacheIsNotStale()
    {
        File.WriteAllText(Refresher.CachePath(), """{"fetched_at_ms":0,"usage":{}}""");
        Assert.False(Refresher.IsStale());
    }

    [Fact]
    public void OldCacheIsStale()
    {
        var path = Refresher.CachePath();
        File.WriteAllText(path, """{"fetched_at_ms":0,"usage":{}}""");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-46)); // just past the 45s TTL
        Assert.True(Refresher.IsStale());
    }

    [Fact]
    public void MissingFailMarkerMeansNoCooldown()
    {
        Assert.False(Refresher.InFailureCooldown());
    }

    [Fact]
    public void FreshFailMarkerMeansInCooldown()
    {
        File.WriteAllText(Refresher.CachePath() + ".fail", "");
        Assert.True(Refresher.InFailureCooldown());
    }

    [Fact]
    public void OldFailMarkerHasExpiredCooldown()
    {
        var path = Refresher.CachePath() + ".fail";
        File.WriteAllText(path, "");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-121)); // past the 120s cooldown
        Assert.False(Refresher.InFailureCooldown());
    }

    [Fact]
    public void ReadCacheReturnsUsageFromAWellFormedFile()
    {
        WriteCacheSecure(Refresher.CachePath(),
            """{"fetched_at_ms":0,"usage":{"extra_usage":{"is_enabled":true,"used_credits":42,"currency":"EUR"}}}""");

        var usage = Refresher.ReadCache();

        Assert.NotNull(usage);
        Assert.True(usage!.ExtraUsage!.IsEnabled);
        Assert.Equal(42, usage.ExtraUsage.UsedCredits);
    }

    [Fact]
    public void ReadCacheReturnsNullForMissingFile()
    {
        Assert.Null(Refresher.ReadCache());
    }

    [Fact]
    public void ReadCacheReturnsNullForCorruptFile()
    {
        WriteCacheSecure(Refresher.CachePath(), "not json at all");
        Assert.Null(Refresher.ReadCache());
    }

    [Fact]
    public void ReadCacheRejectsASymlinkedCacheFile()
    {
        var path = Refresher.CachePath();
        var decoyPath = Path.Combine(_tempTmpDir, "decoy.json");
        File.WriteAllText(decoyPath, """{"fetched_at_ms":0,"usage":{"extra_usage":{"is_enabled":true,"used_credits":999999}}}""");

        File.CreateSymbolicLink(path, decoyPath);

        Assert.Null(Refresher.ReadCache());
    }

    [Fact]
    public void ReadCacheRejectsAFileWithWrongPermissions()
    {
        if (OperatingSystem.IsWindows()) return;

        var path = Refresher.CachePath();
        File.WriteAllText(path, """{"fetched_at_ms":0,"usage":{"extra_usage":{"is_enabled":true,"used_credits":42}}}""");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        Assert.Null(Refresher.ReadCache());
    }

    [Fact]
    public void ReadCacheAcceptsAFileWithCorrectPermissions()
    {
        if (OperatingSystem.IsWindows()) return;

        var path = Refresher.CachePath();
        File.WriteAllText(path, """{"fetched_at_ms":0,"usage":{"extra_usage":{"is_enabled":true,"used_credits":42}}}""");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var usage = Refresher.ReadCache();
        Assert.NotNull(usage);
        Assert.Equal(42, usage!.ExtraUsage!.UsedCredits);
    }
}

/// <summary>
/// Groups every test class that mutates process-wide environment variables
/// into one xunit collection, so they never run concurrently with each other.
/// </summary>
[CollectionDefinition("EnvironmentVariables", DisableParallelization = true)]
public class EnvironmentVariablesCollection;
