#!/usr/bin/env python3
"""Normalise the captured frames and copy them into docs/previews/.

Frames with the same number of lines are padded to a common height, so a
section of the gallery doesn't jitter as glyph ascenders and descenders come
and go (an ASCII frame has no tall icons; a Nerd Font one does).
"""

import json
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import pngtrim
from pngmeasure import read

HERE = Path(__file__).resolve().parent
BUILD = HERE / "build"
DOCS = HERE.parents[1] / "docs" / "previews"


def main():
    frames = json.loads((BUILD / "frames.json").read_text())
    DOCS.mkdir(parents=True, exist_ok=True)

    by_lines = {}
    for f in frames:
        png = BUILD / "png" / f"{f['name']}.png"
        if png.exists():
            by_lines.setdefault(f["lines"], []).append(png)

    for lines, pngs in sorted(by_lines.items()):
        tallest = max(read(p)[1] for p in pngs)
        for png in pngs:
            pngtrim.pad_to(png, tallest)
        print(f"{len(pngs):>2} frame(s) of {lines} line(s) -> height {tallest}")

    copied = 0
    for png in sorted((BUILD / "png").glob("*.png")):
        if png.name.startswith("hero-"):
            continue  # the README's hero is a real screenshot; these are spares
        shutil.copy2(png, DOCS / png.name)
        copied += 1
    print(f"\n{copied} images -> {DOCS.relative_to(HERE.parents[1])}")


if __name__ == "__main__":
    main()
