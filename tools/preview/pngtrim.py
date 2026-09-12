#!/usr/bin/env python3
"""Cut the spare row/column back out of a captured frame.

The terminal is sized larger than the content on purpose -- a last line that
exactly fills the width would wrap-scroll the first line off the top, and
xterm centres whatever grid it ends up with, so the landing position can't be
predicted from the tape. The slack is therefore removed by finding the ink.

Plain frames are cropped to the ink plus a fixed padding. Framed ones (window
bar, border radius, margin) keep their outer edge strips and have the empty
middle cut out instead, so the chrome survives on all four sides.
"""

import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from pngmeasure import read  # noqa: E402


def _write(path, w, h, ch, rows):
    color = {1: 0, 2: 4, 3: 2, 4: 6}[ch]
    raw = bytearray()
    for row in rows:
        raw.append(0)  # filter: none
        raw += row

    def chunk(typ, body):
        return (struct.pack('>I', len(body)) + typ + body
                + struct.pack('>I', zlib.crc32(typ + body) & 0xffffffff))

    Path(path).write_bytes(
        b'\x89PNG\r\n\x1a\n'
        + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, color, 0, 0, 0))
        + chunk(b'IDAT', zlib.compress(bytes(raw), 9))
        + chunk(b'IEND', b''))


def _ink_box(w, h, ch, rows, bg, tol=14):
    top = bottom = left = right = None
    for y, row in enumerate(rows):
        first = None
        for x in range(w):
            px = row[x * ch:x * ch + 3]
            if any(abs(px[k] - bg[k]) > tol for k in range(3)):
                first = x
                break
        if first is None:
            continue
        last = first
        for x in range(w - 1, first, -1):
            px = row[x * ch:x * ch + 3]
            if any(abs(px[k] - bg[k]) > tol for k in range(3)):
                last = x
                break
        top = y if top is None else top
        bottom = y
        left = first if left is None else min(left, first)
        right = last if right is None else max(right, last)
    return top, bottom, left, right


def ink_crop(path, pad):
    """Crop to the content plus a uniform margin of `pad` pixels."""
    w, h, ch, rows = read(path)
    bg = tuple(rows[h - 3][0:3])
    top, bottom, left, right = _ink_box(w, h, ch, rows, bg)
    if top is None:
        raise SystemExit(f"{path}: no content found")
    y0, y1 = max(0, top - pad), min(h, bottom + pad + 1)
    x0, x1 = max(0, left - pad), min(w, right + pad + 1)
    out = [row[x0 * ch:x1 * ch] for row in rows[y0:y1]]
    _write(path, x1 - x0, y1 - y0, ch, out)
    return x1 - x0, y1 - y0


def framed_crop(path, pad, edge):
    """Cut the empty middle out, keeping `edge` pixels of chrome on each side."""
    w, h, ch, rows = read(path)
    bg = tuple(rows[h // 2][(w // 2) * ch:(w // 2) * ch + 3])
    top, bottom, left, right = _ink_box(w, h, ch, rows, bg)
    if top is None:
        raise SystemExit(f"{path}: no content found")
    keep_rows = rows[:bottom + pad + 1] + rows[max(bottom + pad + 1, h - edge):]
    x_cut = right + pad + 1
    out = [row[:x_cut * ch] + row[max(x_cut, w - edge) * ch:] for row in keep_rows]
    new_w = x_cut + max(0, w - max(x_cut, w - edge))
    _write(path, new_w, len(out), ch, out)
    return new_w, len(out)


def pad_to(path, height):
    """Grow an image to `height` with its own background, content centred."""
    w, h, ch, rows = read(path)
    if h >= height:
        return w, h
    bg = rows[0][0:ch]
    blank = bytearray(bg * w)
    extra = height - h
    top = extra // 2
    out = [bytearray(blank) for _ in range(top)] + rows + [bytearray(blank) for _ in range(extra - top)]
    _write(path, w, height, ch, out)
    return w, height


if __name__ == '__main__':
    print(ink_crop(sys.argv[1], int(sys.argv[2])))
