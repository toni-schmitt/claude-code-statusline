#!/usr/bin/env python3
"""Screenshot each generated ANSI frame in a real terminal.

VHS drives ttyd + a headless browser: the frames are rendered by xterm.js, an
actual terminal emulator, not by a drawing library. Sizes are computed from
measured cell geometry so every image is cropped tight to its content.

Note: vhs 0.12.0 has a regression that makes it exit 0 and write nothing
(charmbracelet/vhs#787), so this uses a local v0.11.0 in build/bin.
"""

import json
import math
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import pngtrim

HERE = Path(__file__).resolve().parent
BUILD = HERE / "build"
BIN = BUILD / "bin"

# 28px type gives ~1800px-wide images for a ~900px slot in the README: exactly
# 2x, so they stay sharp on retina displays without shipping resolution no
# screen can use.
FONT_SIZE = 28
LINE_HEIGHT = 1.2
PADDING = 28
# Cell geometry per em, measured off a calibration grid of block glyphs
# rendered by xterm.js in JetBrainsMono Nerd Font.
CELL_W = FONT_SIZE * 0.61494
ROW_H = FONT_SIZE * 1.59028     # at LineHeight 1.2
WINDOW_BAR = 42
MARGIN = 24

THEME = ('{ "name": "ember", "background": "#161616", "foreground": "#c9c9c9", '
         '"cursor": "#161616", "selection": "#3a3a3a", "black": "#161616", '
         '"red": "#d75f5f", "green": "#87af87", "yellow": "#d7af87", "blue": "#87afd7", '
         '"magenta": "#d7afd7", "cyan": "#87d7d7", "white": "#c9c9c9", '
         '"brightBlack": "#5f5f5f", "brightRed": "#ff8787", "brightGreen": "#afd7af", '
         '"brightYellow": "#ffd7af", "brightBlue": "#afd7ff", "brightMagenta": "#ffafff", '
         '"brightCyan": "#afffff", "brightWhite": "#ffffff" }')


def tape(frame, out_png, chrome=False):
    # One spare row and column: a last line that exactly fills the terminal
    # width would otherwise wrap-scroll the first line off the top. The slack
    # is cut back out of the PNG afterwards.
    cols, rows = frame["width"] + 2, frame["lines"] + 2
    width = math.ceil(cols * CELL_W) + 2 * PADDING
    height = math.ceil(rows * ROW_H) + 2 * PADDING
    if chrome:
        width += 2 * MARGIN
        height += WINDOW_BAR + 2 * MARGIN
    lines = [
        'Set Shell "bash"',
        'Set FontFamily "JetBrainsMono Nerd Font"',
        f"Set FontSize {FONT_SIZE}",
        f"Set LineHeight {LINE_HEIGHT}",
        f"Set Padding {PADDING}",
        f"Set Width {width}",
        f"Set Height {height}",
        f"Set Theme {THEME}",
    ]
    if chrome:
        lines += [
            "Set WindowBar Colorful",
            f"Set WindowBarSize {WINDOW_BAR}",
            "Set BorderRadius 16",
            f"Set Margin {MARGIN}",
            'Set MarginFill "#0d0d0d"',
        ]
    ansi = f"ansi/{frame['name']}.ansi"
    lines += [
        f"Output txt/{frame['name']}.txt",
        "Hide",
        # $(cat) strips the trailing newline: with the terminal sized to exactly
        # the content's rows, one stray newline would scroll the top line away.
        f'Type `clear; printf \'\\033[?25l\'; printf \'%s\' "$(cat {ansi})"; sleep 600`',
        "Enter",
        "Sleep 2s",
        "Show",
        "Sleep 1s",
        f"Screenshot {out_png}",
        "Sleep 1s",   # vhs#540: a trailing Screenshot is sometimes dropped
    ]
    return "\n".join(lines) + "\n"


def trim(png, frame, chrome):
    """Cut the terminal's slack back off the image."""
    if not chrome:
        return pngtrim.ink_crop(png, PADDING)
    return pngtrim.framed_crop(png, PADDING, MARGIN + PADDING)


def main(argv):
    chrome_only = "--chrome" in argv
    names = [a for a in argv if not a.startswith("--")]
    frames = json.loads((BUILD / "frames.json").read_text())
    if names:
        frames = [f for f in frames if f["name"] in names]

    for d in ("tapes", "png", "txt"):
        (BUILD / d).mkdir(exist_ok=True)

    for frame in frames:
        for chrome in ([True] if chrome_only else [False]):
            suffix = "-window" if chrome else ""
            out_png = f"png/{frame['name']}{suffix}.png"
            tape_path = BUILD / "tapes" / f"{frame['name']}{suffix}.tape"
            tape_path.write_text(tape(frame, out_png, chrome))
            subprocess.run([str(BIN / "vhs"), str(tape_path.relative_to(BUILD))],
                           cwd=BUILD, check=True,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            png = BUILD / out_png
            if not png.exists():
                print(f"{frame['name']+suffix:38s} MISSING")
                continue
            size = trim(png, frame, chrome)
            print(f"{frame['name']+suffix:38s} {frame['width']:>3}x{frame['lines']}  {size[0]}x{size[1]}")


if __name__ == "__main__":
    main(sys.argv[1:])
