#!/usr/bin/env python3
"""Packs every icon into one texture atlas plus a JSON map, for the WebGL build.

WHY AN ATLAS. A web build that fetches 362 separate PNGs pays 362 round trips before the inventory
can draw, and on a cold cache over a mobile connection that is the difference between a game that
opens and a game people close. One texture is one request, and the GPU can batch every icon into a
single draw call instead of rebinding a texture per tile.

The map is the standard {name: {x, y, w, h}} shape, so a Unity SpriteAtlas, a PixiJS spritesheet or
a hand-rolled UV lookup can all read it.

Deliberately keyed by FILE STEM, which for items is the artKey. That is what makes 104 sigil ids
resolve to 29 regions.

Regenerate after replacing placeholders with real art. Uses the stdlib only, like the placeholder
generator, so it runs on a bare Python.
"""
import io
import json
import math
import pathlib
import struct
import zlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
ICONS = ROOT / "assets" / "icons"
OUT_PNG = ICONS / "atlas.png"
OUT_MAP = ICONS / "atlas.json"

PAD = 2      # transparent gutter, so bilinear filtering cannot bleed a neighbour into a tile
CELL = 128   # atlas cell size; inventory tiles draw at 64-96px, so 128 is ample and keeps the
             # texture inside the 4096 limit every WebGL target supports


def _read_png(path):
    """Minimal RGBA PNG reader. Handles all five scanline filters, so it copes with real art
    exported by an image tool as well as the flat placeholders this project generates."""
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("%s is not a PNG" % path)
    pos = 8
    w = h = None
    idat = bytearray()
    while pos < len(data):
        (length,) = struct.unpack(">I", data[pos:pos + 4])
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        if tag == b"IHDR":
            w, h, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if (depth, color, interlace) != (8, 6, 0):
                raise ValueError("%s: expected 8-bit RGBA, non-interlaced" % path.name)
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            break
        pos += 12 + length

    raw = zlib.decompress(bytes(idat))
    stride = w * 4
    rows = []
    prev = bytearray(stride)
    i = 0
    for _ in range(h):
        f = raw[i]; i += 1
        line = bytearray(raw[i:i + stride]); i += stride
        if f == 1:
            for x in range(4, stride): line[x] = (line[x] + line[x - 4]) & 0xFF
        elif f == 2:
            for x in range(stride): line[x] = (line[x] + prev[x]) & 0xFF
        elif f == 3:
            for x in range(stride):
                a = line[x - 4] if x >= 4 else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 0xFF
        elif f == 4:
            for x in range(stride):
                a = line[x - 4] if x >= 4 else 0
                b = prev[x]
                c = prev[x - 4] if x >= 4 else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 0xFF
        elif f != 0:
            raise ValueError("%s: unsupported filter %d" % (path.name, f))
        rows.append(line)
        prev = line
    return w, h, rows


def _resample(src, w, h, size):
    """Nearest-neighbour to a square cell. Icons are flat colour with hard edges, which is exactly
    the case where nearest keeps the edges crisp and a smooth filter would soften them."""
    out = []
    for y in range(size):
        sy = min(h - 1, y * h // size)
        row = src[sy]
        line = bytearray(size * 4)
        for x in range(size):
            sx = min(w - 1, x * w // size)
            line[x * 4:x * 4 + 4] = row[sx * 4:sx * 4 + 4]
        out.append(line)
    return out


def write_png(path, width, height, pixels):
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        raw.extend(pixels[y])

    def chunk(tag, body):
        d = tag + body
        return struct.pack(">I", len(body)) + d + struct.pack(">I", zlib.crc32(d) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    path.write_bytes(png)


def main():
    tiles = []
    for family in sorted(p.name for p in ICONS.iterdir() if p.is_dir()):
        for f in sorted((ICONS / family).glob("*.png")):
            tiles.append((family, f.stem, f))
    if not tiles:
        raise SystemExit("no icons found under assets/icons/")

    # Every tile is resampled to one cell size. Sources are a mix — 512px real art beside 64px
    # placeholders, and they arrive mixed for as long as the art pass is unfinished — so keying the
    # cell off the first file and skipping anything that disagrees would silently drop whichever
    # kind sorted second. The atlas is a runtime artifact anyway: build it at display resolution and
    # leave the sources at whatever the artist exported.
    cell = CELL
    cell_w = cell_h = cell + PAD
    cols = int(math.ceil(math.sqrt(len(tiles))))
    rows_n = int(math.ceil(len(tiles) / cols))
    atlas_w, atlas_h = cols * cell_w, rows_n * cell_h

    canvas = [bytearray(atlas_w * 4) for _ in range(atlas_h)]
    index = {}
    resampled = 0

    for n, (family, stem, path) in enumerate(tiles):
        w, h, src = _read_png(path)
        if (w, h) != (cell, cell):
            src = _resample(src, w, h, cell)
            resampled += 1
        cx, cy = (n % cols) * cell_w, (n // cols) * cell_h
        for y in range(cell):
            canvas[cy + y][cx * 4:(cx + cell) * 4] = src[y]
        index[stem] = {"x": cx, "y": cy, "w": cell, "h": cell, "family": family}

    write_png(OUT_PNG, atlas_w, atlas_h, canvas)
    io.open(OUT_MAP, "w", encoding="utf-8", newline="\n").write(json.dumps({
        "image": "atlas.png",
        "size": {"w": atlas_w, "h": atlas_h},
        "cell": {"w": cell, "h": cell, "padding": PAD},
        "count": len(index),
        "frames": index,
    }, indent=2) + "\n")

    loose = sum(p.stat().st_size for _, _, p in tiles)
    print("atlas: %dx%d, %d icons, %d requests -> 1" % (atlas_w, atlas_h, len(index), len(tiles)))
    print("  %s  %.0f KB   (loose files total %.0f KB)" % (
        OUT_PNG.name, OUT_PNG.stat().st_size / 1024, loose / 1024))
    print("  %s  %.0f KB" % (OUT_MAP.name, OUT_MAP.stat().st_size / 1024))
    print("  cell %dpx, %d tile(s) resampled to fit" % (cell, resampled))


if __name__ == "__main__":
    main()
