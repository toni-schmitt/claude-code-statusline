# Ember

A two-line status line for [Claude Code](https://code.claude.com), plus a companion renderer for subagent rows. Line one is who and where. Line two is what it's costing you.

![Ember status line, rendered at the bottom of a Claude Code session](docs/statusline.png)

Native AOT, self-contained binary — no .NET runtime, no interpreter, nothing to install on the machine that runs it.

## What it shows

**Line 1** — model (coloured by an ember gradient) and reasoning effort, project and git branch, session duration, context window usage.

**Line 2** — 5-hour and 7-day rate-limit usage as half-cell bars, with this session's own contribution highlighted as an ember-coloured tail on the 5-hour bar; countdowns to each window's reset; and a spend estimate that escalates to a real credits readout, or a limit-reached banner, when it matters.

On an API key or any account without Claude's rate-limit data, line 1 is unaffected and line 2 degrades gracefully to just the spend estimate. On a narrow terminal, both lines shed segments in a defined order rather than wrapping or truncating mid-glyph.

The companion (`ember-subagent`) renders the same grammar, narrower, for each subagent row Claude Code shows while a task is running.

## Installing

### Prerequisites

- macOS or Linux (Native AOT supports Windows too, but distribution here targets only these two platforms)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source
- A [Nerd Font](https://www.nerdfonts.com) in your terminal for the default icon set — e.g. `brew install --cask font-jetbrains-mono-nerd-font`, then select *JetBrainsMono Nerd Font* in your terminal. Without one, pass `--icons=unicode` (works with the preinstalled Menlo on macOS) or `--icons=ascii`.

### Build from source

There's no published release yet, so building from source is the way to install this today:

```sh
git clone https://github.com/toni-schmitt/claude-code-statusline.git
cd claude-code-statusline

dotnet publish -c Release -r <RID> -p:PublishAot=true src/Ember/Ember.csproj -o out
dotnet publish -c Release -r <RID> -p:PublishAot=true src/Ember.Subagent/Ember.Subagent.csproj -o out
```

`<RID>` is your platform's runtime identifier: `osx-arm64`, `osx-x64`, `linux-x64`, or `linux-arm64`. This produces two self-contained native binaries — `out/ember` and `out/ember-subagent` — with no further dependencies.

Put both somewhere on your `PATH`:

```sh
mkdir -p ~/.local/bin
cp out/ember out/ember-subagent ~/.local/bin/
```

## Configuring Claude Code

Let `ember` write the config for you:

```sh
ember --install
```

This merges the status line block into your existing `~/.claude/settings.json` (rather than overwriting it) using the binary's own resolved path.

Or configure it by hand:

```jsonc
// ~/.claude/settings.json
{
  "statusLine": {
    "type": "command",
    "command": "/absolute/path/to/ember",
    "refreshInterval": 15
  },
  "subagentStatusLine": {
    "type": "command",
    "command": "/absolute/path/to/ember-subagent"
  }
}
```

`refreshInterval: 15` matters: the session clock and both countdowns are time-based and would otherwise only update when Claude Code re-runs the command for an unrelated reason. Fifteen seconds keeps them visibly live without spending more than a fraction of a percent of a core (native startup is ~8ms).

### Flags

Set these on the command itself, e.g. `"command": "/path/to/ember --icons=unicode"`.

| Flag | Default | Effect |
|---|---|---|
| `--icons=nerd\|unicode\|ascii` | `nerd` | Icon set |
| `--git-dirty` | off | Reserved for a future git dirty-flags feature; currently a no-op |
| `--weekly-per-model` | off | Reserved for future per-model weekly rate-limit windows; currently a no-op |
| `--refresh` | — | Runs the detached usage-cache refresh; not for `settings.json` |
| `--install` | — | Writes the settings block above into `settings.json` |

### Trying it without a live session

```sh
echo '{"model":{"display_name":"Opus 5"},"effort":{"level":"xhigh"},
"workspace":{"project_dir":"/x/claude-code-statusline","current_dir":"/x"},
"session_id":"test","cost":{"total_cost_usd":0.82,"total_duration_ms":5040000},
"context_window":{"used_percentage":12},
"rate_limits":{"five_hour":{"used_percentage":85,"resets_at":1789142400},
"seven_day":{"used_percentage":44,"resets_at":1789574400}}}' | ./out/ember
```

## How it works, briefly

- Everything on line 1, and the bars/countdowns/spend estimate on line 2, comes straight from the JSON Claude Code writes to stdin on every render — no network call is ever on the render path.
- Real credit figures and the escalated "at-limit" / "blocked" / "credits" banners come from a detached background process that polls Claude's usage endpoint at most once every 45 seconds, caching the result in `$TMPDIR`. A render reads that cache and returns immediately; it never waits on the network.
- If the API is unreachable, expired, or simply hasn't been polled yet, nothing on the line breaks — it just reports the more conservative, stdin-only truth instead.

## Development

```sh
dotnet build Ember.slnx
dotnet test tests/Ember.Tests/Ember.Tests.csproj
```

## License

MIT — see [LICENSE](LICENSE).

## AI Honesty

This project was completely generated by LLMs.

<a href="https://www.aihonestybadge.com" target="_blank" rel="noopener"><img src="https://www.aihonestybadge.com/badges/ai-generated.svg" alt="AI Generated Badge" style="max-width: 190px; height: auto;" /></a>
