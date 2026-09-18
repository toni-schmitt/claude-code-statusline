#!/usr/bin/env python3
"""Build the README's preview gallery from the captured frames."""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from pngmeasure import read  # noqa: E402

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DOCS = ROOT / "docs" / "previews"

SECTIONS = [
    ("state-", "Everyday states",
     "A Max account at various points in a session."),
    ("spend-", "The spend slot",
     "Its five tenants, in the order you'd meet them."),
    ("icons-", "Icon sets",
     "The same session with each `--icons` value."),
    ("subagent-", "Subagent rows",
     "`ember-subagent` renders the same grammar, narrower, one row per task."),
    ("model-", "The model gradient",
     "Model names are coloured by an ember gradient across their own characters."),
    ("line1-", "Missing or unusual data",
     "Segments disappear rather than render as blanks or placeholders."),
    ("width-", "Narrow terminals",
     "One session, shed segment by segment. Nothing wraps and nothing truncates mid-glyph."),
]



# Every image is scaled by one shared factor rather than to a fixed width, so
# a 48-column frame actually looks narrower than a 120-column one.
WIDEST = 900


def img(frame, sizes, scale):
    w = round(sizes[frame["name"]][0] * scale)
    return (f'<img src="docs/previews/{frame["name"]}.png" width="{w}" '
            f'alt="{frame["caption"]}">')


def render_gallery():
    frames = json.loads((HERE / "build" / "frames.json").read_text())
    sizes = {}
    for f in frames:
        p = DOCS / f"{f['name']}.png"
        if p.exists():
            w, h, _, _ = read(p)
            sizes[f["name"]] = (w, h)

    scale = WIDEST / max(w for w, _ in sizes.values())

    out = []
    for prefix, title, blurb in SECTIONS:
        picked = [f for f in frames if f["name"].startswith(prefix) and f["name"] in sizes]
        if not picked:
            continue
        out.append(f"<details>\n<summary><b>{title}</b></summary>\n")
        out.append(f"\n{blurb}\n")
        for f in picked:
            out.append(f"\n{f['caption']}\n\n{img(f, sizes, scale)}\n")
        out.append("\n</details>\n")
    return "".join(out)


if __name__ == "__main__":
    print(render_gallery())
