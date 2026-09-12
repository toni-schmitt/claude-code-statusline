#!/usr/bin/env python3
"""Splice the freshly captured gallery into README.md.

Idempotent: it replaces the existing hero image and the whole "What it looks
like" section, so it can be re-run after every capture.
"""

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from gallery import render_gallery  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
README = ROOT / "README.md"

# A real screenshot of a live session, not a generated frame -- the generated
# hero frames are still available under tools/preview if this ever changes.
HERO = "docs/statusline.png"
HERO_ALT = ("The Ember status line in a live Claude Code session, with its "
            "subagent rows beneath it")
HERO_WIDTH = 900

INTRO = """## What it looks like

Every frame below is real output: the binaries in this repo, fed a synthetic Claude Code
payload in a throwaway sandbox and screenshotted in an actual terminal emulator. Nothing
here is drawn or mocked up — see [`tools/preview`](tools/preview) for how they are produced
and how to regenerate them.

"""


def main():
    s = README.read_text()

    hero = f'<img src="{HERO}" width="{HERO_WIDTH}" alt="{HERO_ALT}">'
    s, n = re.subn(r'<img src="docs/previews/hero[^>]*>', hero, s, count=1)
    if not n:
        raise SystemExit("hero image not found in README")

    section = INTRO + render_gallery().strip() + "\n\n"
    start = s.index("## What it looks like")
    end = s.index("## Installing", start)
    s = s[:start] + section + s[end:]

    README.write_text(s)
    print(f"README.md updated ({section.count('<img')} gallery images)")


if __name__ == "__main__":
    main()
