#!/usr/bin/env python3
"""Cuts a generated contact sheet into one square transparent PNG per icon.

An image model hands back one sheet of eight icons; the engine needs eight files named for their
content ids. Doing that by hand is roughly an hour per batch in an image editor, and there are
twenty-three batches.

HOW IT FINDS THE ICONS. Everything that is not background becomes a mask, the mask is dilated so
nearby blobs merge, and each connected island is one icon. The dilation is the whole trick: a pair
of boots is two blobs and one icon, and so is a pair of gloves. Radius is tunable because a sheet
with tight gutters needs a smaller one than a sheet with generous gaps.

BACKGROUND. Real alpha is used when the sheet has any. Image models often return opaque white
instead, or draws the transparency CHECKERBOARD as real pixels. Either way the background is
inferred from the outer ring and keyed out — which also fixes the "white box on a dark
inventory tile" problem before it reaches the game.

Components are found on a quarter-scale mask. At full resolution the flood fill is slow in pure
Python for no benefit: a bounding box does not need pixel precision, and the crop is taken from the
full-resolution original.

  python tools/art/split_sheet.py sheet.png --names gear_conscript_helm,gear_conscript_chest,...
  python tools/art/split_sheet.py sheet.png --out assets/icons/gear --size 512

Without --names the crops are written numbered, so a sheet whose layout does not match the prompt
order can still be cut and renamed by eye.
"""
import argparse
import collections
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import pnglib

SCALE = 4          # mask is built at 1/SCALE for component finding
WHITE = 238        # a channel at or above this, on all three, counts as background
ALPHA_BG = 16


def border_colours(w, h, rows, bands=6):
    """Colour histogram of the outer ring, which is background by definition."""
    hist = collections.Counter()
    for y in list(range(bands)) + list(range(h - bands, h)):
        r = rows[y]
        for x in range(0, w, 2):
            hist[bytes(r[x * 4:x * 4 + 3])] += 1
    for y in range(0, h, 2):
        r = rows[y]
        for x in list(range(bands)) + list(range(w - bands, w)):
            hist[bytes(r[x * 4:x * 4 + 3])] += 1
    return hist


def build_mask(w, h, rows):
    """Returns (mask, keyed). mask[y][x] is 1 for foreground, at full resolution.

    Real alpha is used whenever the sheet has any. Otherwise the background is inferred from the
    outer ring rather than assumed to be white, because an image model asked for a transparent
    background sometimes draws the CHECKERBOARD — the visual convention for transparency — as actual
    pixels. That is two flat greys on a regular grid, so a white-only key leaves it in place and the
    icon ships with a chequered box behind it.

    Taking the dominant ring colours covers white, any flat colour, and the checkerboard alike, and
    needs no special case for a pattern whose exact greys vary between models.
    """
    has_alpha = any(rows[y][x * 4 + 3] < 250 for y in range(0, h, 7) for x in range(0, w, 7))
    mask = [bytearray(w) for _ in range(h)]
    if has_alpha:
        for y in range(h):
            r, m = rows[y], mask[y]
            for x in range(w):
                if r[x * 4 + 3] > ALPHA_BG:
                    m[x] = 1
        return mask, None

    hist = border_colours(w, h, rows)
    total = sum(hist.values())
    bg = []
    covered = 0
    for colour, n in hist.most_common(4):
        bg.append(colour)
        covered += n
        if covered >= total * 0.90:
            break
    keyed = set(bg)

    for y in range(h):
        r, m = rows[y], mask[y]
        for x in range(w):
            i = x * 4
            if bytes(r[i:i + 3]) not in keyed:
                m[x] = 1
    return mask, keyed


def downscale(mask, w, h):
    sw, sh = (w + SCALE - 1) // SCALE, (h + SCALE - 1) // SCALE
    small = [bytearray(sw) for _ in range(sh)]
    for y in range(h):
        row, srow = mask[y], small[y // SCALE]
        for x in range(w):
            if row[x]:
                srow[x // SCALE] = 1
    return small, sw, sh


def dilate(mask, w, h, r):
    """Separable box dilation. Merges blobs that belong to one object, such as a pair of boots."""
    tmp = [bytearray(w) for _ in range(h)]
    for y in range(h):
        row, out = mask[y], tmp[y]
        runs = [x for x in range(w) if row[x]]
        for x in runs:
            for k in range(max(0, x - r), min(w, x + r + 1)):
                out[k] = 1
    final = [bytearray(w) for _ in range(h)]
    for y in range(h):
        if not any(tmp[y]):
            continue
        for k in range(max(0, y - r), min(h, y + r + 1)):
            fk, ty = final[k], tmp[y]
            for x in range(w):
                if ty[x]:
                    fk[x] = 1
    return final


def components(mask, w, h, min_cells):
    seen = [bytearray(w) for _ in range(h)]
    boxes = []
    for sy in range(h):
        for sx in range(w):
            if not mask[sy][sx] or seen[sy][sx]:
                continue
            q = collections.deque([(sx, sy)])
            seen[sy][sx] = 1
            x0 = x1 = sx
            y0 = y1 = sy
            n = 0
            while q:
                x, y = q.popleft()
                n += 1
                if x < x0: x0 = x
                if x > x1: x1 = x
                if y < y0: y0 = y
                if y > y1: y1 = y
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and mask[ny][nx] and not seen[ny][nx]:
                        seen[ny][nx] = 1
                        q.append((nx, ny))
            if n >= min_cells:
                boxes.append((x0, y0, x1, y1))
    return boxes


def reading_order(boxes):
    """Row bands top to bottom, left to right inside a band."""
    if not boxes:
        return boxes
    heights = sorted(b[3] - b[1] for b in boxes)
    band = max(4, heights[len(heights) // 2] // 2)
    out = sorted(boxes, key=lambda b: (b[1], b[0]))
    rows, cur, top = [], [], out[0][1]
    for b in out:
        if b[1] - top > band:
            rows.append(cur); cur = []; top = b[1]
        cur.append(b)
    rows.append(cur)
    return [b for row in rows for b in sorted(row, key=lambda b: b[0])]


def isolate(mask, x0, y0, x1, y1):
    """Largest connected island inside a grid cell, as (box, keep).

    A grid cell almost always clips a sliver off whichever neighbour leans over the boundary, and
    trimming to *any* content in the cell drags that sliver into the crop. Keeping only the biggest
    island discards it, and `keep` then masks the stray pixels out of the crop as well as the box.
    """
    seen = {}
    best_n, best = 0, None
    for sy in range(y0, y1 + 1):
        for sx in range(x0, x1 + 1):
            if not mask[sy][sx] or (sx, sy) in seen:
                continue
            q = collections.deque([(sx, sy)])
            seen[(sx, sy)] = 1
            cells = []
            bx0 = bx1 = sx
            by0 = by1 = sy
            while q:
                x, y = q.popleft()
                cells.append((x, y))
                if x < bx0: bx0 = x
                if x > bx1: bx1 = x
                if y < by0: by0 = y
                if y > by1: by1 = y
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if x0 <= nx <= x1 and y0 <= ny <= y1 and mask[ny][nx] and (nx, ny) not in seen:
                        seen[(nx, ny)] = 1
                        q.append((nx, ny))
            if len(cells) > best_n:
                best_n, best = len(cells), ((bx0, by0, bx1, by1), set(cells))
    return best if best else ((x0, y0, x1, y1), None)


def crop_square(rows, w, h, box, bg, size, keep=None):
    x0, y0, x1, y1 = box
    cw, ch = x1 - x0 + 1, y1 - y0 + 1
    side = max(cw, ch)
    pad_x, pad_y = (side - cw) // 2, (side - ch) // 2
    buf = [bytearray(side * 4) for _ in range(side)]
    for y in range(ch):
        src = rows[y0 + y]
        dst = buf[pad_y + y]
        for x in range(cw):
            si, di = (x0 + x) * 4, (pad_x + x) * 4
            r, g, b, a = src[si], src[si + 1], src[si + 2], src[si + 3]
            if bg is not None and bytes((r, g, b)) in bg:
                a = 0
            elif keep is not None and (x0 + x, y0 + y) not in keep:
                a = 0                     # a sliver of the neighbour that leaned into this cell
            dst[di:di + 4] = bytes((r, g, b, a))

    out = [bytearray(size * 4) for _ in range(size)]
    for y in range(size):
        sy = min(side - 1, y * side // size)
        srow, orow = buf[sy], out[y]
        for x in range(size):
            sx = min(side - 1, x * side // size)
            orow[x * 4:x * 4 + 4] = srow[sx * 4:sx * 4 + 4]
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sheet", type=pathlib.Path)
    ap.add_argument("--names", default="", help="comma-separated ids, in reading order")
    ap.add_argument("--out", type=pathlib.Path, default=pathlib.Path("."))
    ap.add_argument("--size", type=int, default=512)
    ap.add_argument("--gap", type=int, default=10,
                    help="dilation radius in sheet pixels; raise it to merge a pair of boots, "
                         "lower it if two neighbouring icons are being merged")
    ap.add_argument("--min-area", type=float, default=0.15,
                    help="discard islands smaller than this fraction of the median island")
    ap.add_argument("--grid", default="",
                    help="COLSxROWS, e.g. 4x2. Cuts on an even grid and trims each cell to its "
                         "content instead of hunting for islands. Always yields exactly COLS*ROWS "
                         "crops, so it is the reliable mode when the sheet was generated as a grid")
    args = ap.parse_args()

    w, h, rows = pnglib.read(args.sheet)
    mask, keyed = build_mask(w, h, rows)

    if args.grid:
        cols, _, rws = args.grid.lower().partition("x")
        cols, rws = int(cols), int(rws)
        full, keeps = [], []
        for ry in range(rws):
            for cx in range(cols):
                cx0, cy0 = cx * w // cols, ry * h // rws
                cx1, cy1 = (cx + 1) * w // cols - 1, (ry + 1) * h // rws - 1
                box, keep = isolate(mask, cx0, cy0, cx1, cy1)
                if keep is None:
                    continue          # empty cell: a grid with more slots than icons
                full.append(box); keeps.append(keep)
    else:
        small, sw, sh = downscale(mask, w, h)
        grown = dilate(small, sw, sh, max(1, args.gap // SCALE))
        boxes = components(grown, sw, sh, min_cells=4)
        if not boxes:
            raise SystemExit("no icons found — is the sheet blank?")
        areas = sorted((b[2] - b[0] + 1) * (b[3] - b[1] + 1) for b in boxes)
        floor = areas[len(areas) // 2] * args.min_area
        boxes = [b for b in boxes if (b[2] - b[0] + 1) * (b[3] - b[1] + 1) >= floor]
        boxes = reading_order(boxes)
        full = [(max(0, b[0] * SCALE), max(0, b[1] * SCALE),
                 min(w - 1, b[2] * SCALE + SCALE - 1), min(h - 1, b[3] * SCALE + SCALE - 1))
                for b in boxes]
        keeps = [None] * len(full)

    names = [n.strip() for n in args.names.split(",") if n.strip()]
    if names and len(names) != len(full):
        # Refuse rather than write. Writing numbered crops instead dumps junk into whatever --out
        # points at, which on a real run is the asset folder: eight files to find and delete next to
        # a hundred real ones.
        raise SystemExit(
            "REFUSING TO WRITE: %d names given, %d icons found. Nothing was written.\n"
            "  The sheet is probably not a clean grid. An object crossing a cell line is cut into\n"
            "  two crops, and a batch that does not fill its grid invites the model to spread\n"
            "  objects across the boundaries.\n"
            "  Re-run without --names, pointing --out at a scratch folder, to see what it found."
            % (len(names), len(full)))

    print("sheet %dx%d, background=%s, %d icons\n" % (
        w, h,
        "alpha" if keyed is None else "%d flat colour(s) keyed out" % len(keyed),
        len(full)))
    for n, box in enumerate(full):
        name = names[n] if names else "crop_%02d" % (n + 1)
        img = crop_square(rows, w, h, box, keyed, args.size, keeps[n])
        dest = args.out / (name + ".png")
        pnglib.write(dest, args.size, args.size, img)
        print("  %-32s from (%4d,%4d)-(%4d,%4d)  ->  %s" % (
            name + ".png", box[0], box[1], box[2], box[3], dest.parent))


if __name__ == "__main__":
    main()
