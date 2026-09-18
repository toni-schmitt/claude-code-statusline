#!/usr/bin/env python3
"""Render every status line state in the README gallery, for real.

Nothing here is drawn by hand. Each frame is the byte-for-byte stdout of the
actual `ember` / `ember-subagent` binaries, fed a synthetic Claude Code stdin
payload inside a throwaway sandbox: $HOME, $CLAUDE_CONFIG_DIR and $TMPDIR are
all redirected, so the real config, state file and usage cache are never read
or written.

States that depend on accumulated state (the `+N%` session share, the "today"
total, credits deltas) are produced the way they occur in life -- by replaying
a short sequence of payloads through the same sandbox and keeping the last
frame.

Output: build/ansi/<name>.ansi plus build/frames.json for capture.py.
"""

import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import time
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BUILD = ROOT / "tools" / "preview" / "build"
# Sandboxes live outside the repo on purpose: GitHead walks up from the project
# directory looking for a .git, so a sandbox nested under this checkout would
# find *this* repo's branch and the "not a git repo" frame would show `main`.
SANDBOX_ROOT = Path(tempfile.gettempdir()) / "ember-preview-sandboxes"
EMBER = ROOT / "out" / "ember"
SUBAGENT = ROOT / "out" / "ember-subagent"

NOW = int(time.time())
MIN = 60
HOUR = 3600
DAY = 86400
ACCOUNT_UUID = "8f14e45f-ea8f-4b62-9e2a-1d0c6a7b3e55"
ANSI_RE = re.compile(r"\x1b\[[0-9;]*m")


# --------------------------------------------------------------------------
# stdin payloads
# --------------------------------------------------------------------------

def session(
    *,
    sid="preview",
    model="Opus 5",
    effort="xhigh",
    project="claude-code-statusline",
    branch="main",
    duration_min=84.0,
    cost=0.82,
    ctx=12.0,
    ctx_tokens=None,
    five=None,
    five_reset_min=46,
    seven=None,
    seven_reset_hours=101,
):
    """One Claude Code status line payload (§3's stdin contract)."""
    p = {
        "session_id": sid,
        "model": {"display_name": model},
        "cost": {
            "total_duration_ms": int(duration_min * 60_000),
            "total_cost_usd": cost,
        },
    }
    if effort is not None:
        p["effort"] = {"level": effort}
    if ctx is not None:
        cw = {"used_percentage": ctx}
        if ctx_tokens is not None:
            cw["total_input_tokens"] = ctx_tokens
        p["context_window"] = cw
    limits = {}
    if five is not None:
        limits["five_hour"] = {
            "used_percentage": five,
            "resets_at": NOW + int(five_reset_min * MIN),
        }
    if seven is not None:
        limits["seven_day"] = {
            "used_percentage": seven,
            "resets_at": NOW + int(seven_reset_hours * HOUR),
        }
    if limits:
        p["rate_limits"] = limits
    return p


def usage(*, credits_enabled=None, used_credits=None, currency="USD", monthly_limit=None):
    """A usage-API response for the refresher cache (§4.1)."""
    extra = {}
    if credits_enabled is not None:
        extra["is_enabled"] = credits_enabled
    if used_credits is not None:
        extra["used_credits"] = used_credits
    if currency is not None:
        extra["currency"] = currency
    if monthly_limit is not None:
        extra["monthly_limit"] = monthly_limit
    return {"extra_usage": extra} if extra else {}


def tasks(rows, columns=120):
    return {"columns": columns, "tasks": rows}


def task(tid, name, *, status="running", model="claude-sonnet-5", effort="high",
         ctx_size=200_000, tokens=54_000, description=""):
    t = {
        "id": tid,
        "name": name,
        "status": status,
        "description": description,
        "contextWindowSize": ctx_size,
        "tokenCount": tokens,
    }
    if model is not None:
        t["model"] = model
    if effort is not None:
        t["effort"] = effort
    return t


# --------------------------------------------------------------------------
# scenarios
# --------------------------------------------------------------------------

@dataclass
class Frame:
    """One captured image: a name, a caption, and how to produce its bytes."""
    name: str
    caption: str
    columns: int = 120
    icons: str = "nerd"
    steps: list = field(default_factory=list)       # list of (payload, usage|None)
    subagent: dict | None = None                    # subagent payload, rendered below
    subagent_only: bool = False
    branch: str | None = "main"                     # None => no git repo at all
    detached_sha: str | None = None
    group: str = "states"


def sandbox(name, branch, detached_sha):
    sb = SANDBOX_ROOT / name
    if sb.exists():
        shutil.rmtree(sb)
    (sb / "home").mkdir(parents=True)
    (sb / "config").mkdir(parents=True)
    (sb / "tmp").mkdir(parents=True)
    proj = sb / "work" / "claude-code-statusline"
    proj.mkdir(parents=True)
    if branch is not None or detached_sha is not None:
        git = proj / ".git"
        git.mkdir()
        head = f"{detached_sha}\n" if detached_sha else f"ref: refs/heads/{branch}\n"
        (git / "HEAD").write_text(head)
    (sb / "home" / ".claude.json").write_text(
        json.dumps({"oauthAccount": {"accountUuid": ACCOUNT_UUID}})
    )

    if branch is None and detached_sha is None:
        # The frame is meant to have no branch at all, so make sure nothing
        # above it does either -- §12.4 walks up until it finds a .git.
        for parent in [proj, *proj.parents]:
            if (parent / ".git").exists():
                raise SystemExit(
                    f"{name}: a .git at {parent} would give this frame a branch it shouldn't have")

    return sb, proj


def cache_path(sb):
    digest = hashlib.sha256(str(sb / "config").encode()).hexdigest()[:16]
    return sb / "tmp" / f"ember-usage-{digest}.json"


def write_cache(sb, payload):
    path = cache_path(sb)
    if payload is None:
        path.unlink(missing_ok=True)
        return
    path.write_text(json.dumps({"fetched_at_ms": NOW * 1000, "usage": payload}))
    os.chmod(path, 0o600)  # §10: the refresher refuses a cache that isn't owner-exclusive


def env_for(sb, columns):
    return {
        "HOME": str(sb / "home"),
        "CLAUDE_CONFIG_DIR": str(sb / "config"),
        "TMPDIR": str(sb / "tmp"),
        "COLUMNS": str(columns),
        "PATH": "/usr/bin:/bin",
        "TZ": "UTC",
    }


def render(frame: Frame) -> str:
    sb, proj = sandbox(frame.name, frame.branch, frame.detached_sha)
    env = env_for(sb, frame.columns)
    out = ""

    if not frame.subagent_only:
        for payload, usage_payload in frame.steps:
            payload = json.loads(json.dumps(payload))
            payload["workspace"] = {
                "project_dir": str(proj),
                "current_dir": str(proj),
            }
            write_cache(sb, usage_payload)
            result = subprocess.run(
                [str(EMBER), f"--icons={frame.icons}"],
                input=json.dumps(payload),
                env=env,
                capture_output=True,
                text=True,
                check=True,
            )
            out = result.stdout.rstrip("\n")

    if frame.subagent is not None:
        rows = subprocess.run(
            [str(SUBAGENT), f"--icons={frame.icons}"],
            input=json.dumps(frame.subagent),
            env=env,
            capture_output=True,
            text=True,
            check=True,
        ).stdout
        contents = [json.loads(line)["content"] for line in rows.splitlines() if line.strip()]
        block = "\n".join(contents)
        out = f"{out}\n\n{block}" if out else block

    return out


# A single believable session, reused wherever the scenario is about
# something other than the numbers themselves.
def typical(**kw):
    base = dict(sid="cur", model="Opus 5", effort="xhigh", duration_min=84,
                cost=0.82, ctx=12, five=85, five_reset_min=46, seven=44,
                seven_reset_hours=101)
    base.update(kw)
    # Claude Code reports total_input_tokens alongside used_percentage (§3),
    # so a realistic frame carries both. Derived from a 200k window unless the
    # caller supplies its own token count.
    if base.get("ctx") is not None and "ctx_tokens" not in kw:
        base["ctx_tokens"] = round(base["ctx"] / 100 * 200_000)
    return session(**base)


def with_history(final, *, prior_today=2.59, share_from=None, usage_chain=(None, None, None)):
    """Replay the payloads that make `today` and `+N%` land on real values.

    A prior session contributes the rest of the day's spend; the current
    session is seen once at a lower 5h reading -- which is what sets its
    watermark -- before the frame we keep. `usage_chain` is the refresher
    cache as of each of those three renders.
    """
    steps = []
    if prior_today is not None:
        steps.append((session(sid="prev", cost=prior_today, ctx=None), usage_chain[0]))

    first = json.loads(json.dumps(final))
    first["cost"] = dict(first["cost"], total_cost_usd=0.0)
    if share_from is not None:
        five = first.get("rate_limits", {}).get("five_hour")
        if five is not None:
            five["used_percentage"] = share_from
    steps.append((first, usage_chain[1]))
    steps.append((final, usage_chain[2]))
    return steps


def credits_chain(used_before_day, used_at_session_start, used_now, currency="USD"):
    return (
        usage(credits_enabled=True, used_credits=used_before_day, currency=currency),
        usage(credits_enabled=True, used_credits=used_at_session_start, currency=currency),
        usage(credits_enabled=True, used_credits=used_now, currency=currency),
    )


SUBAGENT_ROWS = [
    task("t1", "auth-refactor", status="running", model="claude-opus-5", effort="xhigh",
         tokens=54_000, description="Refactoring AuthService.cs and middleware"),
    task("t2", "test-writer", status="running", model="claude-haiku-4-5-20251001", effort=None,
         tokens=21_000, description="Writing integration tests for TokenManager"),
    task("t3", "code-reviewer", status="done", model="claude-sonnet-5", effort="high",
         tokens=82_000, description="Reviewed AuthService.cs changes"),
    task("t4", "doc-writer", status="queued", model="claude-sonnet-5", effort=None,
         tokens=0, description="Waiting on the refactor to land"),
    task("t5", "bench-runner", status="failed", model="claude-opus-5", effort="high",
         tokens=131_000, description="Benchmark harness exited non-zero"),
]


FRAMES = [
    # ---- hero -----------------------------------------------------------
    Frame("hero-statusline", "The status line as it appears at the bottom of a session",
          group="hero",
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("hero-with-subagent-rows", "The status line with three subagent rows beneath it",
          group="hero",
          steps=with_history(typical(), prior_today=2.59, share_from=63),
          subagent=tasks(SUBAGENT_ROWS[:3])),

    # ---- everyday states ------------------------------------------------
    Frame("state-typical-max-session", "A Max account mid-session: 5h window at 85%, 22% of it this session",
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("state-fresh-session", "Six minutes in: both windows barely touched",
          steps=with_history(
              typical(model="Sonnet 5", duration_min=6, cost=0.95, ctx=7, five=9,
                      five_reset_min=268, seven=31, seven_reset_hours=101),
              prior_today=9.59, share_from=5)),
    Frame("state-quiet-start", "A brand-new session on a quiet day",
          steps=with_history(
              typical(model="Sonnet 5", effort="high", duration_min=3, cost=0.04, ctx=3,
                      five=2, five_reset_min=291, seven=6, seven_reset_hours=152),
              prior_today=None, share_from=0)),
    Frame("state-long-session", "Four hours deep: the 5h window is nearly spent and turns critical",
          steps=with_history(
              typical(effort="max", duration_min=277, cost=6.41, ctx=68, five=96,
                      five_reset_min=23, seven=88, seven_reset_hours=39),
              prior_today=4.12, share_from=55)),
    Frame("state-weekly-window-critical", "Plenty of 5h headroom left, but the weekly window is almost gone",
          steps=with_history(
              typical(duration_min=41, cost=1.18, ctx=22, five=34, five_reset_min=214,
                      seven=97, seven_reset_hours=14),
              prior_today=3.90, share_from=21)),
    Frame("state-context-nearly-full", "Context window at 91%, shortly before a compact",
          steps=with_history(
              typical(duration_min=132, cost=3.07, ctx=91, five=72, five_reset_min=96,
                      seven=58, seven_reset_hours=77),
              prior_today=5.50, share_from=48)),

    # ---- accounts without rate-limit data -------------------------------
    Frame("state-no-rate-limits", "An API-key account: no rate-limit data, so line two degrades to the spend estimate",
          steps=with_history(typical(five=None, seven=None), prior_today=2.59)),
    Frame("state-five-hour-only", "Only the 5h window reported so far, early in a session",
          steps=with_history(typical(duration_min=11, cost=0.21, ctx=8, five=17,
                                     five_reset_min=243, seven=None),
                             prior_today=1.02, share_from=9)),

    # ---- the spend slot's five tenants -----------------------------------
    Frame("spend-estimate", "The default: a dim, self-measured estimate of session and daily spend",
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("spend-five-hour-limit", "5h window exhausted, with no credits information to go on",
          steps=with_history(typical(duration_min=193, cost=4.88, ctx=57, five=100,
                                     five_reset_min=67, seven=76, seven_reset_hours=52),
                             prior_today=6.20, share_from=88)),
    Frame("spend-limit-reached", "5h window exhausted and extra usage is switched off: nothing to do but wait",
          steps=with_history(typical(duration_min=193, cost=4.88, ctx=57, five=100,
                                     five_reset_min=67, seven=76, seven_reset_hours=52),
                             prior_today=6.20, share_from=88,
                             usage_chain=(usage(credits_enabled=False),) * 3)),
    Frame("spend-seven-day-limit", "The weekly window exhausted while the 5h one is barely touched: the banner names the window you are waiting on",
          steps=with_history(typical(duration_min=193, cost=3.42, ctx=57, five=5,
                                     five_reset_min=59, seven=100, seven_reset_hours=17),
                             prior_today=6.20, share_from=5)),
    Frame("spend-seven-day-limit-reached", "The weekly window exhausted with extra usage switched off: the countdown runs to the weekly reset, not the 5h one",
          steps=with_history(typical(duration_min=193, cost=3.42, ctx=57, five=5,
                                     five_reset_min=59, seven=100, seven_reset_hours=17),
                             prior_today=6.20, share_from=5,
                             usage_chain=(usage(credits_enabled=False),) * 3)),
    Frame("spend-credits", "Past the 5h window on extra usage: real credit figures replace the estimate",
          steps=with_history(typical(duration_min=193, cost=4.88, ctx=57, five=100,
                                     five_reset_min=67, seven=76, seven_reset_hours=52),
                             prior_today=6.20, share_from=88,
                             usage_chain=credits_chain(1000, 1525, 1740))),
    Frame("spend-credits-weekly", "Past the weekly window on extra usage: the 5h window has plenty of room, but credits are what is paying for the work",
          steps=with_history(typical(duration_min=193, cost=3.42, ctx=57, five=5,
                                     five_reset_min=59, seven=100, seven_reset_hours=17),
                             prior_today=6.20, share_from=5,
                             usage_chain=credits_chain(2400, 2656, 2790, currency="EUR"))),
    Frame("spend-credits-after-rollover", "Back under every cap after a window rolled over: what the session really cost stays, without the banner",
          steps=with_history(typical(duration_min=241, cost=4.10, ctx=62, five=12,
                                     five_reset_min=284, seven=61, seven_reset_hours=52),
                             prior_today=6.20, share_from=12,
                             usage_chain=credits_chain(2400, 2656, 2790, currency="EUR"))),
    Frame("spend-credits-eur", "The same credits readout for a euro-billed account",
          steps=with_history(typical(duration_min=193, cost=4.88, ctx=57, five=100,
                                     five_reset_min=67, seven=76, seven_reset_hours=52),
                             prior_today=6.20, share_from=88,
                             usage_chain=credits_chain(1000, 1525, 1740, currency="EUR"))),
    Frame("spend-credits-jpy", "A zero-decimal currency: yen figures keep no fractional part",
          steps=with_history(typical(duration_min=193, cost=4.88, ctx=57, five=100,
                                     five_reset_min=67, seven=76, seven_reset_hours=52),
                             prior_today=6.20, share_from=88,
                             usage_chain=credits_chain(120_000, 187_500, 214_000, currency="JPY"))),

    # ---- what line one does with missing or unusual data -----------------
    Frame("line1-no-git-repo", "A directory that isn't a git repo: the branch segment simply isn't there",
          branch=None,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("line1-detached-head", "Detached HEAD: the short SHA stands in for the branch name",
          branch=None, detached_sha="4f2c9ab6d1e0c7b53a8f10d4e6b2c9a7f0d3e1b8",
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("line1-long-branch-name", "A long branch name, untouched: project and branch never shed on width",
          branch="feature/rate-limit-gradient-rework",
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("line1-no-context-window", "Just after /compact, while the context reading is still absent",
          steps=with_history(typical(ctx=None), prior_today=2.59, share_from=63)),
    Frame("line1-no-effort", "A model with no reasoning-effort setting",
          steps=with_history(typical(model="Haiku 4.5", effort=None, ctx=9, cost=0.11),
                             prior_today=0.74, share_from=63)),

    # ---- the model gradient ---------------------------------------------
    Frame("model-opus-5", "Opus 5", group="models",
          steps=with_history(typical(model="Opus 5"), prior_today=2.59, share_from=63)),
    Frame("model-sonnet-5", "Sonnet 5", group="models",
          steps=with_history(typical(model="Sonnet 5"), prior_today=2.59, share_from=63)),
    Frame("model-haiku-45", "Haiku 4.5", group="models",
          steps=with_history(typical(model="Haiku 4.5", effort=None), prior_today=2.59, share_from=63)),
    Frame("model-opus-41", "Opus 4.1", group="models",
          steps=with_history(typical(model="Opus 4.1", effort="high"), prior_today=2.59, share_from=63)),

    # ---- icon sets -------------------------------------------------------
    Frame("icons-nerd", "--icons=nerd (default): a Nerd Font's glyphs", group="icons",
          icons="nerd", steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("icons-unicode", "--icons=unicode: plain Unicode, no Nerd Font needed", group="icons",
          icons="unicode", steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("icons-ascii", "--icons=ascii: pure ASCII, for terminals that can't do better — the spend figures fall back with the icons (`~` for estimates, `|` as separator, ISO codes for currency symbols that aren't ASCII)", group="icons",
          icons="ascii", steps=with_history(typical(), prior_today=2.59, share_from=63)),

    # ---- width degradation, at the column counts where each tier actually flips
    Frame("width-120-columns", "120 columns: everything fits", group="width", columns=120,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("width-100-columns", "100: the 7d countdown is the first thing to go", group="width", columns=100,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("width-93-columns", "93: both bars halve to five cells", group="width", columns=93,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("width-83-columns", "83: the spend slot loses its words", group="width", columns=83,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("width-76-columns", "76: line one starts shedding, beginning with ctx and its token count", group="width", columns=76,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("width-69-columns", "69: the spend slot goes", group="width", columns=69,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("width-58-columns", "58: the session clock goes", group="width", columns=58,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("width-49-columns", "49: the session's share of the 5h window goes", group="width", columns=49,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),
    Frame("width-48-columns", "48: reasoning effort goes — project and branch never shed on width", group="width", columns=48,
          steps=with_history(typical(), prior_today=2.59, share_from=63)),

    # ---- subagent rows ---------------------------------------------------
    Frame("subagent-rows", "ember-subagent: running, queued, done and failed rows", group="subagent",
          subagent_only=True, subagent=tasks(SUBAGENT_ROWS)),
    Frame("subagent-rows-unicode", "The same rows with --icons=unicode", group="subagent",
          subagent_only=True, icons="unicode", subagent=tasks(SUBAGENT_ROWS)),
    Frame("subagent-rows-ascii", "The same rows with --icons=ascii", group="subagent",
          subagent_only=True, icons="ascii", subagent=tasks(SUBAGENT_ROWS)),
    Frame("subagent-rows-narrow", "Subagent rows at 80 columns: the description truncates, nothing else", group="subagent",
          subagent_only=True, columns=80, subagent=tasks(SUBAGENT_ROWS, columns=80)),
]


def main(only=()):
    if not EMBER.exists() or not SUBAGENT.exists():
        raise SystemExit(
            f"build the binaries first: dotnet publish ... -o out  (missing {EMBER} / {SUBAGENT})"
        )

    ansi_dir = BUILD / "ansi"
    ansi_dir.mkdir(parents=True, exist_ok=True)
    manifest = []

    manifest_path = BUILD / "frames.json"
    previous = {f["name"]: f for f in json.loads(manifest_path.read_text())} if (only and manifest_path.exists()) else {}

    for frame in FRAMES:
        if only and frame.name not in only:
            if frame.name in previous:
                manifest.append(previous[frame.name])
            continue
        text = render(frame)
        (ansi_dir / f"{frame.name}.ansi").write_text(text + "\n")
        lines = text.split("\n")
        visible = max(len(ANSI_RE.sub("", line)) for line in lines)
        manifest.append({
            "name": frame.name,
            "caption": frame.caption,
            "group": frame.group,
            "columns": frame.columns,
            "lines": len(lines),
            "width": visible,
            "icons": frame.icons,
        })
        print(f"{frame.name:32s} {len(lines)} line(s)  {visible} cols")

    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"\n{len(manifest)} frames -> {ansi_dir}")


if __name__ == "__main__":
    import sys
    main(only=set(sys.argv[1:]))
