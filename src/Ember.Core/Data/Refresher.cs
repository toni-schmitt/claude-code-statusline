using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core.Data;

/// <summary>The refresher's on-disk cache: the §4.1 response plus when it was fetched.</summary>
public sealed class RefresherCache
{
    [JsonPropertyName("fetched_at_ms")]
    public long FetchedAtMs { get; init; }

    [JsonPropertyName("usage")]
    public UsageResponse? Usage { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RefresherCache))]
public partial class RefresherJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Keeps the usage cache warm from a detached process, §10. A render never
/// blocks on the network: it reads whatever is on disk, and if that is
/// stale it fires a detached <c>--refresh</c> and returns immediately with
/// what it had (rule 1).
/// </summary>
public static class Refresher
{
    private const int TtlSeconds = 45;
    private const int LockStaleSeconds = 60;
    private const int FailureCooldownSeconds = 120;
    private const int ConnectTimeoutSeconds = 2;
    private const int ReadTimeoutSeconds = 3;

    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    public static string CachePath()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Credentials.ConfigDir())))[..16];
        var tmpDir = Environment.GetEnvironmentVariable("TMPDIR") is { Length: > 0 } t ? t : "/tmp";
        return Path.Combine(tmpDir, $"ember-usage-{hash}.json");
    }

    private static string LockPath() => CachePath() + ".lock";
    private static string FailMarkerPath() => CachePath() + ".fail";

    /// <summary>Reads the cache, or null if it is absent, unsafe, or unparseable.</summary>
    public static UsageResponse? ReadCache()
    {
        var path = CachePath();
        try
        {
            if (!IsSafeToRead(path)) return null;
            var cache = JsonSerializer.Deserialize(File.ReadAllText(path), RefresherJsonContext.Default.RefresherCache);
            return cache?.Usage;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsStale()
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return true;
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            return age.TotalSeconds > TtlSeconds;
        }
        catch
        {
            return true;
        }
    }

    public static bool InFailureCooldown()
    {
        try
        {
            var path = FailMarkerPath();
            if (!File.Exists(path)) return false;
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            return age.TotalSeconds < FailureCooldownSeconds;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Rejects a cache file that is a symlink or not owner-exclusive (§10).
    /// On a shared /tmp the filename is derivable, so both checks matter:
    /// the symlink check stops redirection; the mode check (0600) stops a
    /// planted regular file, because an attacker's file must grant
    /// group/other read for the victim to open it, making its mode != 0600.
    /// </summary>
    private static bool IsSafeToRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            if (info.LinkTarget is not null) return false;
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(path);
                if (mode != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Fire-and-forget: spawns <c>&lt;emberPath&gt; --refresh</c>, guarded by a single-flight lock. Never awaited.</summary>
    public static void SpawnDetachedRefresh(string emberPath)
    {
        try
        {
            if (InFailureCooldown()) return;
            if (!TryAcquireLock()) return;

            try
            {
                var psi = new ProcessStartInfo(emberPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                };
                psi.ArgumentList.Add("--refresh");
                var process = Process.Start(psi);
                if (process is not null)
                {
                    process.StandardOutput.Close();
                    process.StandardError.Close();
                    process.StandardInput.Close();
                    process.Dispose();
                }
                else
                {
                    ReleaseLock();
                }
            }
            catch
            {
                ReleaseLock();
            }
        }
        catch
        {
            // A failed spawn changes nothing already on screen (§9.3).
        }
    }

    private static bool TryAcquireLock()
    {
        var lockPath = LockPath();
        try
        {
            // Clean up legacy directory-based locks from previous versions.
            try
            {
                if (Directory.Exists(lockPath)) Directory.Delete(lockPath, true);
            }
            catch { /* best effort */ }

            // Reject symlinks at the lock path — on a shared /tmp a symlink
            // could redirect the stale-lock File.Delete to an unrelated target.
            try
            {
                if (new FileInfo(lockPath) is { Exists: true, LinkTarget: not null })
                {
                    try { File.Delete(lockPath); } catch { }
                    return false;
                }
            }
            catch { /* best effort */ }

            if (File.Exists(lockPath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
                if (age.TotalSeconds < LockStaleSeconds) return false;
                File.Delete(lockPath);
            }
            // FileMode.CreateNew is atomic: fails if the file already exists.
            using var _ = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(lockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ReleaseLock()
    {
        try { File.Delete(LockPath()); } catch { /* best effort */ }
    }

    /// <summary>The body of <c>--refresh</c>: fetch, then write the cache or a failure marker, then release the lock.</summary>
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = Credentials.GetAccessToken();
            if (token is null)
            {
                WriteFailMarker();
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Add("anthropic-beta", "oauth-2025-04-20");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutSeconds + ReadTimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            using var response = await client.GetAsync(UsageEndpoint, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                WriteFailMarker(); // never cache an error body (rule 5)
                return;
            }

            await using var body = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            var usage = await JsonSerializer.DeserializeAsync(body, UsageJsonContext.Default.UsageResponse, linked.Token).ConfigureAwait(false);
            if (usage is null)
            {
                WriteFailMarker();
                return;
            }

            WriteCache(usage);
            ClearFailMarker();
        }
        catch
        {
            WriteFailMarker();
        }
        finally
        {
            ReleaseLock();
        }
    }

    private static void WriteCache(UsageResponse usage)
    {
        try
        {
            var path = CachePath();
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);

            var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.tmp-{Environment.ProcessId}");
            var cache = new RefresherCache { FetchedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Usage = usage };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(cache, RefresherJsonContext.Default.RefresherCache));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // Best effort -- a failed cache write just means the next render tries again.
        }
    }

    private static void WriteFailMarker()
    {
        try { File.WriteAllText(FailMarkerPath(), ""); } catch { /* best effort */ }
    }

    private static void ClearFailMarker()
    {
        try { File.Delete(FailMarkerPath()); } catch { /* best effort */ }
    }
}
