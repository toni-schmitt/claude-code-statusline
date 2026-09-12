"""Minimal PNG reader: just enough to find the ink in a VHS screenshot."""
import struct, zlib, sys

def read(path):
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', 'not a png'
    pos, idat, w = 8, bytearray(), None
    while pos < len(data):
        ln, typ = struct.unpack('>I4s', data[pos:pos+8])
        body = data[pos+8:pos+8+ln]
        if typ == b'IHDR':
            w, h, depth, color, _, _, interlace = struct.unpack('>IIBBBBB', body)
            assert depth == 8 and interlace == 0, (depth, interlace)
        elif typ == b'IDAT':
            idat += body
        pos += 12 + ln
    channels = {0: 1, 2: 3, 4: 2, 6: 4}[color]
    raw = zlib.decompress(bytes(idat))
    stride = w * channels
    out, prev = [], bytearray(stride)
    i = 0
    for _ in range(h):
        f = raw[i]; i += 1
        line = bytearray(raw[i:i+stride]); i += stride
        if f == 1:
            for x in range(channels, stride): line[x] = (line[x] + line[x-channels]) & 255
        elif f == 2:
            for x in range(stride): line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride):
                a = line[x-channels] if x >= channels else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x-channels] if x >= channels else 0
                b = prev[x]; c = prev[x-channels] if x >= channels else 0
                p = a + b - c
                pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        out.append(line); prev = line
    return w, h, channels, out

def ink_bounds(path, bg_tol=14):
    """Rows/cols that contain something other than the flat background."""
    w, h, ch, rows = read(path)
    bg = tuple(rows[h//2][0:3])          # a row of pure background, far below the text
    top = bottom = left = right = None
    for y, row in enumerate(rows):
        for x in range(w):
            px = row[x*ch:x*ch+3]
            if any(abs(px[k]-bg[k]) > bg_tol for k in range(3)):
                if top is None: top = y
                bottom = y
                left = x if left is None else min(left, x)
                right = x if right is None else max(right, x)
                break_row = True
                # scan the rest of the row only for horizontal extent
                for x2 in range(w-1, x, -1):
                    px2 = row[x2*ch:x2*ch+3]
                    if any(abs(px2[k]-bg[k]) > bg_tol for k in range(3)):
                        right = max(right, x2); break
                break
    return dict(w=w, h=h, bg=bg, top=top, bottom=bottom, left=left, right=right)

if __name__ == '__main__':
    for p in sys.argv[1:]:
        print(p, ink_bounds(p))
