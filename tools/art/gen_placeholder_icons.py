#!/usr/bin/env python3
"""Placeholder icon art for all 435 content entries, and the manifest for the real art pass.

WHY PLACEHOLDERS AT ALL. Beta players are about to see this game. 435 blank squares reads as
"unfinished"; 435 distinguishable tiles reads as "art pending", which is the truth. These are
deliberately NOT trying to be the final art — they are trying to be legible at a glance and
obviously provisional, so nobody mistakes one for a finished asset.

WHAT AN ICON ENCODES, in priority order:
  1. RARITY  — the 3px frame + the background tint. This is the single most important read in an
               ARPG inventory and it is carried by the loudest element on the tile.
  2. KIND    — a 16x16 hand-authored pixel glyph (helm, boot, potion, sigil, banner...). Tells you
               what the thing IS without reading the name.
  3. IDENTITY— a per-id accent hue, hashed from the id. Two Blue helms are not the same picture,
               so a player can tell their inventory rows apart before the art lands.

RARITY COLOURS ARE THE CLIENT'S OWN. Lifted from ItemDropOverlay.RarityColor so a placeholder in the
inventory grid frames identically to the drop overlay that announced it. No invented palette.

NO DEPENDENCIES. PNG is a zlib stream plus CRC32 chunks and both live in the stdlib, so this runs on
a bare Python with no pip install. Pixel art is exactly the case where that is easy: no filtering,
one scanline filter byte of 0x00, colour type 6 (RGBA).

DETERMINISTIC. Same id in, same picture out, every run. Regenerating never reshuffles the set, so a
placeholder can be replaced by real art one file at a time without the others moving.

OUTPUT
  assets/icons/<family>/<id>.png    the 64x64 tiles
  assets/icons/MANIFEST.csv         every entry: id, family, kind, rarity, name, description

Prompts for the real art live in ART_BRIEF.md, built by gen_art_brief.py.
"""
import collections
import colorsys
import csv
import hashlib
import io
import json
import pathlib
import struct
import zlib

import pnglib

ROOT = pathlib.Path(__file__).resolve().parents[2]
CONTENT = ROOT / "src" / "ROTA.Api" / "content"
OUT = ROOT / "assets" / "icons"

SCALE = 4          # 16x16 glyph -> 64x64 tile
SIZE = 16 * SCALE
FRAME = 3          # frame thickness in output pixels

# ── the client's own rarity colours (ItemDropOverlay.RarityColor), as 8-bit RGB ───────────────────
RARITY = {
    "Grey":   (153, 153, 153),
    "White":  (235, 235, 235),
    "Green":  (89, 184, 102),
    "Blue":   (84, 140, 242),
    "Purple": (166, 102, 217),
    "Orange": (242, 140, 51),
}
DEFAULT_RARITY = "Grey"

INK = (26, 23, 20)          # 'o' outline
STEEL = (200, 194, 180)     # '#' body
SHINE = (240, 235, 221)     # '*' highlight
GROUND = (18, 16, 14)       # tile background before the rarity tint


# ══ GLYPHS ═══════════════════════════════════════════════════════════════════════════════════════
# 16x16.  '.' transparent  ·  'o' outline  ·  '#' body  ·  '*' highlight  ·  '+' id-derived accent
G = {}

G["head"] = [
    "................",
    "................",
    ".....oooooo.....",
    "....o######o....",
    "...o##****##o...",
    "...o#*####*#o...",
    "...o#*####*#o...",
    "...o########o...",
    "...o#+oooo+#o...",
    "...o#+oooo+#o...",
    "...o##oooo##o...",
    "....o#o..o#o....",
    "....o#o..o#o....",
    ".....oo..oo.....",
    "................",
    "................",
]
G["torso"] = [
    "................",
    "................",
    ".ooo........ooo.",
    "o###oo....oo###o",
    "o####oooooo####o",
    "o##############o",
    "o###*##oo##*###o",
    "o###*#*oo*#*###o",
    "o###*#****#*###o",
    "o####*####*####o",
    ".o###++++++###o.",
    ".o####++++####o.",
    "..o##########o..",
    "..o##########o..",
    "...oooooooooo...",
    "................",
]
G["boots"] = [
    "................",
    "................",
    "..oooo....oooo..",
    "..o##o....o##o..",
    "..o##o....o##o..",
    "..o##o....o##o..",
    "..o##o....o##o..",
    "..o##o....o##o..",
    "..o##oo...o##oo.",
    "..o###o...o###o.",
    "..o#++o...o#++o.",
    ".oo#++oo.oo#++oo",
    ".o######o.o#####",
    ".oooooooo.oooooo",
    "................",
    "................",
]
G["gloves"] = [
    "................",
    "....oo.oo.oo....",
    "...o##o##o##o...",
    "...o##o##o##o...",
    "oo.o##o##o##o...",
    "o#oo########o...",
    "o#o#########o...",
    "o##########o....",
    "o##########o....",
    ".o#########o....",
    ".o#*******#o....",
    ".o#+++++++#o....",
    ".o#########o....",
    "..ooooooooo.....",
    "................",
    "................",
]
G["neck"] = [
    "................",
    "...oo......oo...",
    "..o##o....o##o..",
    "..o##o....o##o..",
    "...o##o..o##o...",
    "....o##oo##o....",
    ".....o####o.....",
    "......oooo......",
    ".....o####o.....",
    "....o#+**+#o....",
    "...o#+****+#o...",
    "...o#++**++#o...",
    "....o#+..+#o....",
    ".....oo##oo.....",
    ".......oo.......",
    "................",
]
G["ring"] = [
    "................",
    "................",
    ".....oooooo.....",
    "....o######o....",
    "...o##oooo##o...",
    "..o##o....o##o..",
    "..o#o......o#o..",
    "..o#o..++..o#o..",
    "..o#o.+**+.o#o..",
    "..o##o+**+o##o..",
    "...o##o++o##o...",
    "....o######o....",
    ".....oooooo.....",
    "................",
    "................",
    "................",
]
G["mount"] = [
    "................",
    "................",
    "............ooo.",
    "...ooooo...o###o",
    "..oo####oooo###o",
    ".o#############o",
    "o##############o",
    "o####+++++#####o",
    "o###+++++++####o",
    "o##############o",
    "o#o##o...o##o##o",
    "o#o.o#o.o#o.o#o.",
    "o#o.o#o.o#o.o#o.",
    "oo..oo..oo..oo..",
    "................",
    "................",
]

G["material"] = [
    "................",
    "................",
    ".......oo.......",
    "......o##o......",
    ".....o#**#o.....",
    "....o##**##o....",
    "...o###**###o...",
    "..o####++####o..",
    "..o###++++###o..",
    "..o##++++++##o..",
    "..o##########o..",
    "...o########o...",
    "....o######o....",
    ".....oooooo.....",
    "................",
    "................",
]
G["sigil"] = [
    "................",
    ".....oooooo.....",
    "...oo######oo...",
    "..o##oo++oo##o..",
    ".o##o.o++o.o##o.",
    ".o#o..o++o..o#o.",
    ".o#o.oo++oo.o#o.",
    ".o#o.++**++.o#o.",
    ".o#o.++**++.o#o.",
    ".o#o.oo++oo.o#o.",
    ".o##o.o++o.o##o.",
    "..o##oo++oo##o..",
    "...oo######oo...",
    ".....oooooo.....",
    "................",
    "................",
]
G["consumable"] = [
    "................",
    "......oooo......",
    "......o##o......",
    "......o##o......",
    ".....o####o.....",
    "....o######o....",
    "...o###**###o...",
    "..o##++++++##o..",
    "..o#++++++++#o..",
    "..o#++++++++#o..",
    "..o#++++++++#o..",
    "..o##++++++##o..",
    "...o########o...",
    "....oooooooo....",
    "................",
    "................",
]
G["statbag"] = [
    "................",
    "................",
    "......oooo......",
    ".....o#oo#o.....",
    "....o##++##o....",
    "...o###++###o...",
    "..o####++####o..",
    "..o##########o..",
    ".o############o.",
    ".o###*####*###o.",
    ".o####****####o.",
    ".o############o.",
    "..o##########o..",
    "...oooooooooo...",
    "................",
    "................",
]

# Magics share a rune-tablet frame so they read as one family; the inner mark separates them.
_TABLET = [
    "................",
    "..oooooooooooo..",
    "..o##########o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o##########o..",
    "..oooooooooooo..",
    "................",
    "................",
]
_MARKS = {
    # 8 wide x 8 tall, dropped in at x=4,y=4
    "damage":   ["...++...", "..+++...", ".++++...", "+++++++.", "..++++++", "...++++.", "...+++..", "...++..."],
    "crit":     ["...++...", "..+**+..", ".+*++*+.", "++++++++", "++++++++", ".+*++*+.", "..+**+..", "...++..."],
    "gold":     ["..++++..", ".+****+.", "+**++**+", "+*+**+*+", "+*+**+*+", "+**++**+", ".+****+.", "..++++.."],
    "leveling": ["...++...", "..+**+..", ".+*++*+.", "++++++++", "...++...", "...++...", "...++...", "..++++.."],
    "utility":  ["+.+..+.+", ".++++++.", "++*..*++", "+*....*+", "+*....*+", "++*..*++", ".++++++.", "+.+..+.+"],
}

# Units share a shield-portrait frame; the inner mark is the battlefield role.
_PORTRAIT = [
    "................",
    "...oooooooooo...",
    "..o##########o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "..o#........#o..",
    "...o#......#o...",
    "....o#....#o....",
    ".....o#..#o.....",
    "......o##o......",
    ".......oo.......",
    "................",
]
_ROLES = {
    "tank":    ["..++++..", ".++++++.", "++*++*++", "++++++++", "++++++++", ".++++++.", "..++++..", "...++..."],
    "melee":   ["......++", ".....++.", "+...++..", ".+.++...", "..+++...", ".+++....", "+++.....", "++......"],
    "ranged":  ["..+++...", ".+...+..", "+.....+.", "+..++..+", "+.++...+", "+++.....", ".+......", "+......."],
    "healer":  ["...++...", "...++...", "...++...", "++++++++", "++++++++", "...++...", "...++...", "...++..."],
    "special": ["...++...", ".+.++.+.", ".++**++.", "++****++", "++****++", ".++**++.", ".+.++.+.", "...++..."],
}

G["legion"] = [
    ".......oo.......",
    ".oooo..oo..oooo.",
    "o####o.oo.o####o",
    "o#++#o.oo.o#++#o",
    "o#++#ooooo.o#++#",
    "o####o###o.o####",
    ".o##o.o#o..o##o.",
    "..oo..o#o...oo..",
    "..oooo###oooo...",
    ".o##########o...",
    ".o#++++++++#o...",
    ".o##########o...",
    "..o########o....",
    "...oooooooo.....",
    ".......oo.......",
    "................",
]
G["recipe"] = [
    "................",
    "................",
    "..........oooo..",
    ".........o####o.",
    "........o##++#o.",
    ".......o##++#o..",
    "..oooo.o#++#o...",
    ".o####oo+#o.....",
    "o######ooo......",
    "o##****#o.......",
    "o#######o.......",
    ".o#####o........",
    "..o###o.........",
    "..ooooo.........",
    "................",
    "................",
]
G["raid"] = [
    "................",
    "..oo......oo....",
    ".o##o....o##o...",
    ".o###oooo###o...",
    "..o########o....",
    ".o##########o...",
    "o####++++####o..",
    "o###++++++###o..",
    "o##++o++o++##o..",
    "o##+o****o+##o..",
    "o###++****+##o..",
    ".o##########o...",
    "..o#o#oo#o#o....",
    "...ooo..ooo.....",
    "................",
    "................",
]


def _compose(frame, mark, ox=4, oy=4):
    """Drop an 8x8 mark into a 16x16 frame."""
    rows = [list(r) for r in frame]
    for y, line in enumerate(mark):
        for x, ch in enumerate(line):
            if ch != ".":
                rows[oy + y][ox + x] = ch
    return ["".join(r) for r in rows]


for _k, _m in _MARKS.items():
    G["magic_" + _k] = _compose(_TABLET, _m)
for _k, _m in _ROLES.items():
    G["unit_" + _k] = _compose(_PORTRAIT, _m, oy=3)


def _validate_glyphs():
    """Every glyph is 16x16 of legal characters. A stray space or a short row is a KeyError or a
    silently truncated icon 400 tiles later, so it fails here instead."""
    legal = set(".o#*+")
    for key, rows in sorted(G.items()):
        if len(rows) != 16:
            raise SystemExit("glyph %r has %d rows, need 16" % (key, len(rows)))
        for i, row in enumerate(rows):
            if len(row) != 16:
                raise SystemExit("glyph %r row %d is %d wide, need 16: %r" % (key, i, len(row), row))
            bad = set(row) - legal
            if bad:
                raise SystemExit("glyph %r row %d has illegal %r: %r" % (key, i, sorted(bad), row))


_validate_glyphs()


# ══ PNG ══════════════════════════════════════════════════════════════════════════════════════════
def write_png(path, width, height, pixels):
    """pixels: flat list of (r,g,b,a). Colour type 6, no filtering — stdlib only."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)                                   # filter type 0 for this scanline
        for x in range(width):
            raw.extend(pixels[y * width + x])

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def accent_for(entry_id):
    """A stable hue per id, so siblings of the same kind and rarity are still telling apart."""
    h = int(hashlib.sha256(entry_id.encode("utf-8")).hexdigest()[:8], 16)
    hue = (h % 360) / 360.0
    r, g, b = colorsys.hls_to_rgb(hue, 0.62, 0.58)
    return (int(r * 255), int(g * 255), int(b * 255))


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def render(entry_id, glyph_key, rarity):
    rc = RARITY.get(rarity, RARITY[DEFAULT_RARITY])
    accent = accent_for(entry_id)
    bg = mix(GROUND, rc, 0.14)
    glyph = G.get(glyph_key) or G["material"]
    palette = {"o": INK, "#": STEEL, "*": SHINE, "+": accent}

    px = [bg + (255,)] * (SIZE * SIZE)

    # glyph, nearest-neighbour to SCALE
    for gy in range(16):
        for gx in range(16):
            ch = glyph[gy][gx]
            if ch == ".":
                continue
            col = palette[ch] + (255,)
            for dy in range(SCALE):
                for dx in range(SCALE):
                    px[(gy * SCALE + dy) * SIZE + (gx * SCALE + dx)] = col

    # rarity frame, drawn in output space so it stays crisp at 3px
    fc = rc + (255,)
    for i in range(FRAME):
        for x in range(SIZE):
            px[i * SIZE + x] = fc
            px[(SIZE - 1 - i) * SIZE + x] = fc
        for y in range(SIZE):
            px[y * SIZE + i] = fc
            px[y * SIZE + (SIZE - 1 - i)] = fc

    # corner notches — reads as a game frame rather than a plain border
    notch = INK + (255,)
    for c in range(5):
        for k in range(5 - c):
            for (cx, cy) in ((c, k), (SIZE - 1 - c, k), (c, SIZE - 1 - k), (SIZE - 1 - c, SIZE - 1 - k)):
                px[cy * SIZE + cx] = notch
    return px


# ══ CONTENT -> ICON JOBS ═════════════════════════════════════════════════════════════════════════
def load(name):
    p = CONTENT / name
    return json.load(io.open(p, encoding="utf-8")) if p.exists() else []


SLOT_GLYPH = {
    "Head": "head", "Torso": "torso", "Boots": "boots", "Gloves": "gloves",
    "Neck": "neck", "Ring1": "ring", "Ring2": "ring", "Mount": "mount",
}
ITEM_GLYPH = {
    "Material": "material", "Sigil": "sigil", "Consumable": "consumable",
    "StatBag": "statbag", "Equipment": "torso",
}


def jobs():
    out = []
    for g in load("gear.json"):
        out.append((g["id"], "gear", SLOT_GLYPH.get(g.get("slot"), "torso"), g.get("rarity", "Grey"),
                    g.get("name", ""), g.get("description", ""), g.get("slot", ""), g.get("setId") or ""))
    # Items address art through artKey, not id: the 104 sigils share 29 pictures because a sigil's
    # art depends on the raid it summons, not on which of the four difficulty tiers it is. Keying by
    # id here would draw the same seal 104 times.
    for i in load("items.json"):
        out.append((i.get("artKey") or i["id"], "item", ITEM_GLYPH.get(i.get("type"), "material"),
                    i.get("rarity", "Grey"), i.get("name", ""), i.get("description", ""),
                    i.get("type", ""), ""))
    for m in load("magics.json"):
        out.append((m["id"], "magic", "magic_" + str(m.get("category", "damage")).lower(),
                    m.get("rarity", "Grey"), m.get("name", ""), m.get("description", ""),
                    m.get("category", ""), ""))
    for u in load("units.json"):
        out.append((u["id"], "unit", "unit_" + str(u.get("role", "melee")).lower(), u.get("rarity", "Grey"),
                    u.get("name", ""), u.get("description", ""),
                    "%s %s" % (u.get("race", ""), u.get("role", "")), ""))
    for l in load("legions.json"):
        out.append((l["id"], "legion", "legion", l.get("rarity", "Blue"), l.get("name", ""),
                    l.get("description", ""), "Legion", ""))
    for r in load("recipes.json"):
        out.append((r["id"], "recipe", "recipe", "Grey", r.get("name", ""), r.get("description", ""),
                    r.get("category", ""), ""))
    for fn in ("raids.json", "guild_raids.json", "gauntlet_raids.json"):
        for r in load(fn):
            out.append((r["id"], "raid", "raid", "Purple", r.get("name", ""), r.get("description", ""),
                        ",".join(r.get("tags", [])) or "Untagged", ""))
    return out




def main():
    rows = jobs()
    seen, dupes = set(), []
    counts = collections.Counter()
    kept = 0
    for (eid, family, glyph, rarity, name, desc, kind, set_id) in rows:
        if eid in seen:
            dupes.append(eid)
            continue
        seen.add(eid)
        target = OUT / family / (eid + ".png")
        # Delivered art is never overwritten. This runs after every content change to fill in
        # tiles for new ids, and a filler that also flattened 127 finished icons back to glyphs
        # would be the most expensive script in the repo.
        if pnglib.is_real_art(target):
            kept += 1
            continue
        write_png(target, SIZE, SIZE, render(eid, glyph, rarity))
        counts[family] += 1

    OUT.mkdir(parents=True, exist_ok=True)
    with io.open(OUT / "MANIFEST.csv", "w", encoding="utf-8", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["id", "family", "kind", "rarity", "set", "name", "description", "icon"])
        for (eid, family, glyph, rarity, name, desc, kind, set_id) in rows:
            w.writerow([eid, family, kind, rarity, set_id, name, desc, "%s/%s.png" % (family, eid)])

    print("icons written: %d  (%s)" % (sum(counts.values()), dict(sorted(counts.items()))))
    print("real art kept:  %d" % kept)
    print("manifest:      assets/icons/MANIFEST.csv")
    if dupes:
        print("shared art keys (expected — sigils collapse by raid): %d" % len(dupes))


if __name__ == "__main__":
    main()
