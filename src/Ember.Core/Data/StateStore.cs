using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core.Data;

/// <summary>Watermarks and daily counters, §8. Holds only derived values -- safe to discard and rebuild.</summary>
public sealed class StateFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("accounts")]
    public Dictionary<string, AccountState> Accounts { get; set; } = new();

    public const int CurrentVersion = 2;
}

public sealed class AccountState
{
    [JsonPropertyName("day")]
    public string Day { get; set; } = "";

    [JsonPropertyName("day_start_credits")]
    public long DayStartCredits { get; set; }

    [JsonPropertyName("today_estimate_usd")]
    public double TodayEstimateUsd { get; set; }

    [JsonPropertyName("sessions")]
    public Dictionary<string, SessionState> Sessions { get; set; } = new();
}

public sealed class SessionState
{
    [JsonPropertyName("five_hour_watermark")]
    public double FiveHourWatermark { get; set; }

    [JsonPropertyName("five_hour_resets_at")]
    public long FiveHourResetsAt { get; set; }

    [JsonPropertyName("session_start_credits")]
    public long SessionStartCredits { get; set; }

    [JsonPropertyName("last_cost_usd")]
    public double LastCostUsd { get; set; }

    [JsonPropertyName("seen_at")]
    public long SeenAt { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StateFile))]
public partial class StateJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Reads and atomically writes <c>statusline-state.json</c>. A corrupt or
/// unrecognised-version file is discarded and rebuilt rather than migrated --
/// it holds only derived values, so the cost is one session's baseline.
/// </summary>
public static class StateStore
{
    public static string GetPath() => Path.Combine(Credentials.ConfigDir(), "statusline-state.json");

    public static StateFile Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new StateFile();

            var state = JsonSerializer.Deserialize(File.ReadAllText(path), StateJsonContext.Default.StateFile);
            if (state is null || state.Version != StateFile.CurrentVersion) return new StateFile();
            return state;
        }
        catch
        {
            return new StateFile();
        }
    }

    /// <summary>Temp file in the same directory, then an atomic rename (§8). Skips the write when the serialized form hasn't changed.</summary>
    public static void Save(string path, StateFile state)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(state, StateJsonContext.Default.StateFile);

            try { if (File.Exists(path) && File.ReadAllText(path) == json) return; }
            catch { /* can't read existing file; proceed with write */ }

            var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.tmp-{Environment.ProcessId}");
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // A failed write costs one render's watermark update, never the render itself (§9.3).
        }
    }

    /// <summary>Keyed by accountUuid/machine so a synced <c>~/.claude</c> never bleeds one machine's baselines into another's (§8).</summary>
    public static string AccountKey(string accountUuid, string machineId) => $"{accountUuid}/{machineId}";

    /// <summary>First 8 hex characters of SHA-256(hostname + /etc/machine-id, or hostname + primary MAC), §8.</summary>
    public static string ComputeMachineId()
    {
        var hostname = Environment.MachineName;
        string material;

        const string machineIdPath = "/etc/machine-id";
        try
        {
            material = File.Exists(machineIdPath)
                ? hostname + File.ReadAllText(machineIdPath).Trim()
                : hostname + PrimaryMacAddress();
        }
        catch
        {
            material = hostname + PrimaryMacAddress();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexStringLower(hash)[..8];
    }

    private static string PrimaryMacAddress()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                      && n.OperationalStatus == OperationalStatus.Up);
            return nic?.GetPhysicalAddress().ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
