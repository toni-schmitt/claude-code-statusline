#!/usr/bin/env sh
# Smoke-test built ember binaries: feed each a synthetic Claude Code payload and
# check the output is the shape we expect. Used by CI after every publish, so a
# binary that builds but cannot render never reaches a release.
#
# Usage: scripts/smoke-test.sh [directory containing ember and ember-subagent]
#
# Runs against a throwaway HOME/CLAUDE_CONFIG_DIR/TMPDIR so it can never read or
# write the real config, state file or usage cache of whoever runs it.

set -eu

dir="${1:-out}"
ember="$dir/ember"
subagent="$dir/ember-subagent"

for bin in "$ember" "$subagent"; do
    if [ ! -x "$bin" ]; then
        echo "smoke-test: missing or non-executable binary: $bin" >&2
        exit 1
    fi
done

sandbox=$(mktemp -d)
trap 'rm -rf "$sandbox"' EXIT
HOME="$sandbox/home"
CLAUDE_CONFIG_DIR="$sandbox/home/.claude"
TMPDIR="$sandbox/tmp"
mkdir -p "$HOME" "$CLAUDE_CONFIG_DIR" "$TMPDIR"
export HOME CLAUDE_CONFIG_DIR TMPDIR

ESC=$(printf '\033')
strip_ansi() { sed "s/${ESC}\[[0-9;]*m//g"; }

fail() { echo "smoke-test: $1" >&2; exit 1; }

# ---------------------------------------------------------------------------
# ember: the status line
# ---------------------------------------------------------------------------
# The model name is gradient-coloured per character, so it is only a contiguous
# substring after the escapes are stripped. Assert on the stripped text.
payload='{"model":{"display_name":"Opus 5"},"effort":{"level":"xhigh"},
"workspace":{"project_dir":"/x/claude-code-statusline","current_dir":"/x"},
"session_id":"smoke-test","cost":{"total_cost_usd":0.82,"total_duration_ms":5040000},
"context_window":{"used_percentage":12},
"rate_limits":{"five_hour":{"used_percentage":85,"resets_at":1789142400},
"seven_day":{"used_percentage":44,"resets_at":1789574400}}}'

raw=$(printf '%s' "$payload" | "$ember") || fail "ember exited non-zero"
[ -n "$raw" ] || fail "ember printed nothing"

plain=$(printf '%s' "$raw" | strip_ansi)
printf 'ember output:\n%s\n\n' "$plain"

printf '%s' "$plain" | grep -q 'Opus 5' || fail "ember output has no model name"
printf '%s' "$plain" | grep -q 'claude-code-statusline' || fail "ember output has no project name"
printf '%s' "$plain" | grep -q 'ctx' || fail "ember output has no context segment"
printf '%s' "$raw" | grep -q "${ESC}\[" || fail "ember emitted no ANSI colour"

lines=$(printf '%s\n' "$raw" | wc -l | tr -d ' ')
[ "$lines" -eq 2 ] || fail "expected 2 status lines, got $lines"

# An empty object must still render a line rather than crash: §15.4's rule is
# that every failure path still prints something.
printf '%s' '{}' | "$ember" >/dev/null || fail "ember failed on an empty payload"

# Unknown flags are ignored, not fatal.
printf '%s' "$payload" | "$ember" --not-a-real-flag >/dev/null \
    || fail "ember failed on an unknown flag"

# ---------------------------------------------------------------------------
# ember-subagent: one JSON line per row
# ---------------------------------------------------------------------------
sub_payload='{"columns":80,"tasks":[{"id":"t1","name":"explore","type":"Explore",
"status":"running","description":"Search the codebase","model":"claude-opus-4-5-20251101",
"contextWindowSize":200000,"tokenCount":24000}]}'

sub_raw=$(printf '%s' "$sub_payload" | "$subagent") || fail "ember-subagent exited non-zero"
[ -n "$sub_raw" ] || fail "ember-subagent printed nothing"

printf 'ember-subagent output:\n%s\n\n' "$sub_raw"

printf '%s' "$sub_raw" | grep -q '"id":"t1"' || fail "ember-subagent did not echo the task id"
printf '%s' "$sub_raw" | grep -q '"content":' || fail "ember-subagent emitted no content field"

printf '%s' '{"columns":80,"tasks":[]}' | "$subagent" >/dev/null \
    || fail "ember-subagent failed on an empty task list"

echo "smoke-test: OK ($dir)"
