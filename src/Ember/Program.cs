using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ember.Core;
using Ember.Core.Data;
using Ember.Core.Render;

namespace Ember;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            switch (options.Mode)
            {
                case EmberMode.Refresh:
                    Refresher.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                    return 0;
                case EmberMode.Install:
                    RunInstall();
                    return 0;
                default:
                    RunRender(options);
                    return 0;
            }
        }
        catch
        {
            // §15.4: a non-zero exit or empty stdout blanks the line. Every
            // failure path must still print something.
            Console.Out.Write("ember: render error\n\n");
            return 0;
        }
    }

    private static void RunRender(Options options)
    {
        string stdinText;
        try { stdinText = Console.In.ReadToEnd(); } catch { stdinText = ""; }

        StdinPayload payload;
        try { payload = JsonSerializer.Deserialize(stdinText, StdinJsonContext.Default.StdinPayload) ?? new StdinPayload(); }
        catch { payload = new StdinPayload(); }

        var icons = Icons.For(options.Icons);
        int columns = int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var n) && n > 0 ? n : 999;

        var walkRoot = payload.Workspace?.Repo is not null ? payload.Workspace.ProjectDir : payload.Workspace?.CurrentDir;
        var branch = GitHead.FindBranch(walkRoot);
        var projectName = ResolveProjectName(payload.Workspace);

        var claudeConfig = LoadClaudeConfig();
        var accountUuid = claudeConfig?.OauthAccount?.AccountUuid
                          ?? claudeConfig?.CachedUsageUtilization?.AccountUuid
                          ?? "unknown";
        var accountKey = StateStore.AccountKey(accountUuid, StateStore.ComputeMachineId());

        var statePath = StateStore.GetPath();
        var state = StateStore.Load(statePath);
        if (!state.Accounts.TryGetValue(accountKey, out var account))
        {
            account = new AccountState();
            state.Accounts[accountKey] = account;
        }

        var sessionId = payload.SessionId ?? "unknown";
        var session = Derived.GetOrCreateSession(account, sessionId, out var isNewSession);
        var share = Derived.FiveHourShare(session, payload.RateLimits?.FiveHour);

        var nowLocal = DateTime.Now;
        var nowUtc = DateTimeOffset.UtcNow;
        var localDate = nowLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dayChanged = Derived.HasLocalDayChanged(account, localDate);

        // Usage cache: never blocks (§10 rule 1). Falls back to Claude
        // Code's own cold-start snapshot (§4.4) only while our own cache is
        // empty and the seed is fresh and matches this account.
        var usage = Refresher.ReadCache();
        if (usage is null)
        {
            var seed = claudeConfig?.CachedUsageUtilization;
            if (seed is not null && accountUuid != "unknown" && seed.AccountUuid == accountUuid
                && nowUtc.ToUnixTimeMilliseconds() - seed.FetchedAtMs <= 10 * 60_000)
            {
                usage = seed.Utilization;
            }
        }
        if (Refresher.IsStale())
        {
            Refresher.SpawnDetachedRefresh(Environment.ProcessPath ?? "ember");
        }

        var creditsResult = Derived.Credits(account, session, isNewSession, dayChanged, usage?.ExtraUsage?.UsedCredits);
        var totalCostUsd = payload.Cost?.TotalCostUsd ?? 0;
        Derived.AccumulateSpendEstimate(account, session, dayChanged, totalCostUsd);
        Derived.CommitLocalDay(account, localDate);
        Derived.TouchAndPruneSessions(account, sessionId, nowUtc.ToUnixTimeSeconds());

        var fiveHourPct = payload.RateLimits?.FiveHour?.UsedPercentage;
        var kind = SpendSlot.Determine(fiveHourPct, usage);
        var fiveHourRemaining = payload.RateLimits?.FiveHour is { } fh
            ? DateTimeOffset.FromUnixTimeSeconds(fh.ResetsAt) - nowUtc
            : TimeSpan.Zero;
        var currency = usage?.ExtraUsage?.Currency ?? "USD";
        var spendData = new SpendSlotData(
            totalCostUsd, account.TodayEstimateUsd, currency,
            creditsResult?.SessionCredits, creditsResult?.TodayCredits, fiveHourRemaining);

        var line1 = Line.ComposeLine1(
            new Line1Input(
                icons, payload.Model?.DisplayName, payload.Effort?.Level,
                projectName, branch, TimeSpan.FromMilliseconds(payload.Cost?.TotalDurationMs ?? 0),
                payload.ContextWindow?.UsedPercentage),
            columns);

        var line2 = Line.ComposeLine2(
            new Line2Input(
                icons, payload.RateLimits?.FiveHour, payload.RateLimits?.SevenDay,
                share, nowUtc, kind, spendData),
            columns);

        // Both lines are fully built strings before anything is written, so
        // a late exception can never leave a half-printed line behind for
        // Main's fallback to collide with (§15.4).
        Console.Out.Write(line1);
        Console.Out.Write('\n');
        Console.Out.Write(line2);
        Console.Out.Write('\n');

        StateStore.Save(statePath, state);
    }

    private static string ResolveProjectName(WorkspaceInfo? ws)
    {
        if (ws?.Repo?.Name is { Length: > 0 } repoName) return repoName;
        var dir = ws?.ProjectDir?.TrimEnd('/', '\\');
        return string.IsNullOrEmpty(dir) ? "" : Path.GetFileName(dir);
    }

    /// <summary>
    /// §4.4/§8: Claude Code's own config file, always at <c>$HOME/.claude.json</c>
    /// regardless of <c>CLAUDE_CONFIG_DIR</c> (which relocates the <c>~/.claude</c>
    /// directory, not this top-level file).
    /// </summary>
    private static ClaudeConfig? LoadClaudeConfig()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), ClaudeConfigJsonContext.Default.ClaudeConfig)
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a Homebrew Cellar path back to the stable symlink that points at it.
    /// <para>
    /// Homebrew installs into <c>&lt;prefix&gt;/Cellar/&lt;formula&gt;/&lt;version&gt;/bin</c> and links
    /// <c>&lt;prefix&gt;/bin</c> at it. <see cref="Environment.ProcessPath"/> reports the *resolved*
    /// path, so running <c>/opt/homebrew/bin/ember --install</c> would otherwise write the
    /// version-pinned Cellar path into settings.json — and the next <c>brew upgrade</c> deletes
    /// that directory, leaving Claude Code pointed at a binary that no longer exists.
    /// </para>
    /// <para>
    /// The link is only trusted when it resolves back to this exact binary, so a same-named
    /// executable from a different formula can never be written instead. Anything that is not a
    /// Cellar path is returned unchanged.
    /// </para>
    /// </summary>
    internal static string PreferStableBinPath(string exePath)
    {
        try
        {
            var full = Path.GetFullPath(exePath);
            var name = Path.GetFileName(full);
            if (name.Length == 0) return exePath;

            var marker = $"{Path.DirectorySeparatorChar}Cellar{Path.DirectorySeparatorChar}";
            var idx = full.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return exePath;

            var candidate = Path.Combine(full[..idx], "bin", name);
            if (!File.Exists(candidate)) return exePath;

            var target = File.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName ?? candidate;
            return Path.GetFullPath(target) == full ? candidate : exePath;
        }
        catch
        {
            // Never let path-tidying break --install; the resolved path still works today.
            return exePath;
        }
    }

    /// <summary>§15's install block, merging rather than overwriting. Best-effort: unconditionally overwrites only the two statusline keys, leaving the rest of settings.json untouched.</summary>
    private static void RunInstall()
    {
        try
        {
            var settingsPath = Path.Combine(Credentials.ConfigDir(), "settings.json");
            var exePath = PreferStableBinPath(Environment.ProcessPath ?? "ember");
            var exeDir = Path.GetDirectoryName(exePath) ?? "";
            var subagentPath = Path.Combine(exeDir, "ember-subagent");

            JsonObject root;
            try
            {
                var docOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                root = File.Exists(settingsPath)
                    ? JsonNode.Parse(File.ReadAllText(settingsPath), documentOptions: docOptions)?.AsObject() ?? new JsonObject()
                    : new JsonObject();
            }
            catch
            {
                Console.Error.WriteLine($"ember --install: {settingsPath} is not valid JSON. Fix it and retry.");
                return;
            }

            root["statusLine"] = new JsonObject
            {
                ["type"] = "command",
                ["command"] = exePath,
                ["refreshInterval"] = 15,
            };
            root["subagentStatusLine"] = new JsonObject
            {
                ["type"] = "command",
                ["command"] = subagentPath,
            };

            var dir = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Wrote {settingsPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ember --install failed: {ex.Message}");
        }
    }
}
