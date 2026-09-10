#!/usr/bin/env python3
"""Read and write 8-bit RGBA PNGs with the standard library alone.

Shared by build_atlas.py and split_sheet.py. Deliberately dependency-free, like the placeholder
generator, so the whole art pipeline runs on a bare Python with no pip install.

The reader handles all five scanline filters rather than only the flat filter-0 the generator emits,
because sheets exported by an image tool or an image model use whichever filter compresses best.
"""
import struct
import zlib


def read(path):
    """Returns (width, height, rows) where each row is a bytearray of RGBA bytes."""
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("%s is not a PNG" % path)

    pos, width, height, palette, trns = 8, None, None, None, None
    color = depth = None
    idat = bytearray()
    while pos < len(data):
        (length,) = struct.unpack(">I", data[pos:pos + 4])
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        if tag == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if depth != 8 or interlace != 0:
                raise ValueError("%s: only 8-bit non-interlaced PNGs are supported" % path.name)
        elif tag == b"PLTE":
            palette = body
        elif tag == b"tRNS":
            trns = body
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            break
        pos += 12 + length

    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}.get(color)
    if channels is None:
        raise ValueError("%s: unsupported colour type %s" % (path.name, color))

    raw = zlib.decompress(bytes(idat))
    stride = width * channels
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(height):
        f = raw[i]; i += 1
        line = bytearray(raw[i:i + stride]); i += stride
        if f == 1:
            for x in range(channels, stride):
                line[x] = (line[x] + line[x - channels]) & 0xFF
        elif f == 2:
            for x in range(stride):
                line[x] = (line[x] + prev[x]) & 0xFF
        elif f == 3:
            for x in range(stride):
                a = line[x - channels] if x >= channels else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 0xFF
        elif f == 4:
            for x in range(stride):
                a = line[x - channels] if x >= channels else 0
                b = prev[x]
                c = prev[x - channels] if x >= channels else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 0xFF
        elif f != 0:
            raise ValueError("%s: unsupported filter %d" % (path.name, f))
        rows.append(line)
        prev = line

    return width, height, [_to_rgba(r, width, channels, color, palette, trns) for r in rows]


def _to_rgba(line, width, channels, color, palette, trns):
    if color == 6:
        return line
    out = bytearray(width * 4)
    for x in range(width):
        if color == 2:
            r, g, b, a = line[x * 3], line[x * 3 + 1], line[x * 3 + 2], 255
        elif color == 0:
            r = g = b = line[x]; a = 255
        elif color == 4:
            r = g = b = line[x * 2]; a = line[x * 2 + 1]
        else:                                   # indexed
            idx = line[x]
            r, g, b = palette[idx * 3], palette[idx * 3 + 1], palette[idx * 3 + 2]
            a = trns[idx] if trns and idx < len(trns) else 255
        out[x * 4:x * 4 + 4] = bytes((r, g, b, a))
    return out


def write(path, width, height, rows):
    """rows: an iterable of bytearrays of RGBA bytes, one per scanline."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        raw.extend(rows[y])

    def chunk(tag, body):
        d = tag + body
        return struct.pack(">I", len(body)) + d + struct.pack(">I", zlib.crc32(d) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)
