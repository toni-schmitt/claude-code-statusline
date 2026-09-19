using System.Diagnostics;
using System.Globalization;
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

    /// <summary>
    /// How long a cached response stays ground truth, §10 rule 7. Beyond the
    /// TTL the cache is merely due for a refresh and is still served; beyond
    /// this it is no longer evidence of anything and reads as absent.
    /// <para>
    /// The two cannot be one number. The TTL has to be short enough to keep
    /// the figures current, and serving nothing that recently would blank the
    /// slot on every network hiccup. But a refresh that fails keeps failing
    /// only 120s later (<see cref="FailureCooldownSeconds"/>), so an hours-old
    /// cache means the refresher has been failing continuously — and a
    /// <c>is_enabled: false</c> from that far back is a guess, not a fact. It
    /// drives §6's choice between the at-limit and blocked banners, and only
    /// the blocked one asserts that work has stopped.
    /// </para>
    /// <para>
    /// Ten minutes is §4.4's bound on Claude Code's own snapshot, applied to
    /// our cache for the same reason: past it, credit status is unknown.
    /// </para>
    /// </summary>
    private const int MaxAgeSeconds = 600;

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

    /// <summary>
    /// Reads the cache, or null if it is absent, unsafe, unparseable, or older
    /// than <see cref="MaxAgeSeconds"/>. Ageing out reads as absent rather than
    /// as its own state so every caller inherits the cold-cache behaviour it
    /// already handles: §6 degrades credit status to <c>unknown</c>, and
    /// Program falls back to Claude Code's §4.4 snapshot, which may be fresher
    /// than what the refresher last managed to fetch.
    /// </summary>
    public static UsageResponse? ReadCache()
    {
        var path = CachePath();
        try
        {
            if (!IsSafeToRead(path)) return null;
            if (AgeSeconds(path) is not double age || age > MaxAgeSeconds) return null;
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
        var age = AgeSeconds(CachePath());
        return age is not double seconds || seconds > TtlSeconds;
    }

    public static bool InFailureCooldown()
    {
        var age = AgeSeconds(FailMarkerPath());
        return age is double seconds && seconds < FailureCooldownSeconds;
    }

    /// <summary>
    /// Seconds since <paramref name="path"/> was last written, or null when it
    /// does not exist or cannot be stat'd. Last-write time rather than the
    /// cache's own <c>fetched_at_ms</c>: the cache is replaced wholesale by an
    /// atomic rename on every successful fetch, so the two agree, and one
    /// clock source keeps the TTL, the max age and the cooldown comparable.
    /// </summary>
    private static double? AgeSeconds(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds;
        }
        catch
        {
            return null;
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

    /// <summary>
    /// The body of <c>--refresh</c>: fetch, then write the cache or a failure
    /// marker, then release the lock. Returns null on success, otherwise the
    /// reason the fetch failed, as recorded in the marker.
    /// </summary>
    /// <param name="cancellationToken">Cancels the fetch; a cancelled fetch counts as a failure like any other.</param>
    public static async Task<string?> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = Credentials.GetAccessToken();
            if (token is null)
            {
                // Distinct from a rejected token: nothing was found to send.
                // On macOS that is usually the keychain denying `security`,
                // not a signed-out user (§4.3).
                return WriteFailMarker("no-token: no access token in the keychain or credentials file");
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Add("anthropic-beta", "oauth-2025-04-20");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutSeconds + ReadTimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            using var response = await client.GetAsync(UsageEndpoint, linked.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode is false)
            {
                // The status code only: an error body may echo request detail,
                // and nothing from an error response is ever kept (rule 5).
                return WriteFailMarker($"http-{(int)response.StatusCode}: {response.StatusCode}");
            }

            await using var body = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            var usage = await JsonSerializer.DeserializeAsync(body, UsageJsonContext.Default.UsageResponse, linked.Token).ConfigureAwait(false);
            if (usage is null)
            {
                return WriteFailMarker("empty-body: the endpoint returned JSON null");
            }

            WriteCache(usage);
            ClearFailMarker();
            return null;
        }
        catch (OperationCanceledException)
        {
            // The linked source fires on both the caller's token and the
            // §10 rule 6 budget, and a socket timeout surfaces the same way.
            return WriteFailMarker($"timeout: no response within {ConnectTimeoutSeconds + ReadTimeoutSeconds}s");
        }
        catch (Exception ex)
        {
            return WriteFailMarker($"{ex.GetType().Name}: {ex.Message}");
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

    /// <summary>
    /// Records <paramref name="reason"/> in the failure marker and returns it.
    /// <para>
    /// The marker's mtime is what rule 4's cooldown reads; its contents exist
    /// purely so a person can find out why. Without them every failure mode —
    /// a keychain that will not open, an expired token, a 429, a dead network —
    /// looks identical from outside, and the line's only symptom is figures
    /// that quietly stop moving. Never holds a token or a response body.
    /// </para>
    /// </summary>
    /// <param name="reason">A single line: a short kind, a colon, then detail.</param>
    /// <returns><paramref name="reason"/>, unchanged, so callers can return it directly.</returns>
    private static string WriteFailMarker(string reason)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        try { File.WriteAllText(FailMarkerPath(), $"{stamp} {reason}\n"); } catch { /* best effort */ }
        return reason;
    }

    /// <summary>
    /// The last recorded failure, whole line including its timestamp, or null
    /// when the marker is absent or empty. Diagnostic only — no render path
    /// reads it (§9.3).
    /// </summary>
    public static string? LastFailure()
    {
        try
        {
            var path = FailMarkerPath();
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            return text.Length is 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static void ClearFailMarker()
    {
        try { File.Delete(FailMarkerPath()); } catch { /* best effort */ }
    }
}
