using System.Diagnostics;
using System.Text.Json;

namespace Ember.Core.Data;

public static class Credentials
{
    public static string? GetAccessToken()
    {
        if (OperatingSystem.IsMacOS())
        {
            var fromKeychain = TryKeychain();
            if (fromKeychain is not null) return fromKeychain;
        }
        return TryCredentialsFile();
    }

    private static string? TryKeychain()
    {
        try
        {
            var psi = new ProcessStartInfo("security")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("find-generic-password");
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add("Claude Code-credentials");
            psi.ArgumentList.Add("-w");

            using var process = Process.Start(psi);
            if (process is null) return null;

            string output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }

            if (process.ExitCode != 0 || output.Length == 0) return null;
            return ParseAccessToken(output);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryCredentialsFile()
    {
        try
        {
            var path = Path.Combine(ConfigDir(), ".credentials.json");
            if (!File.Exists(path)) return null;
            return ParseAccessToken(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    internal static string? ParseAccessToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) &&
                oauth.TryGetProperty("accessToken", out var token) &&
                token.ValueKind == JsonValueKind.String)
            {
                return token.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string ConfigDir() =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
}
