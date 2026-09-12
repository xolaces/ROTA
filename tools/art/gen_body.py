#!/usr/bin/env python3
"""The paper-doll body: one flat-vector mannequin, and where each slot docks on it.

Phase 1 of the player model (owner 2026-09-11: "go, one body, no gender yet"). The client draws
this figure in the Profile's EQUIPPED card and docks each worn piece's icon on the part of the body
it belongs to — helm on the head, boots at the feet, the mount standing beside. It is a placeholder
in the same sense the icon tiles are: the real body is drawn from the brief's prompt and dropped in
over this file, and the docks are read from the JSON beside it, so the client never hard-codes an
anatomy it cannot see.

Writes:
  assets/icons/body/mannequin.png    512 x 768, transparent, front-facing, arms a little out
  assets/icons/body/mannequin.json   {"w","h","docks":{slot:[x,y] as fractions of w,h}, "order":[...]}

The body folder is served like the icons (/icons/body/…) but is NOT packed into the atlas — a
2:3 figure does not belong in a 128px cell — so build_atlas.py and the icon tests skip it.
"""
import io
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import pnglib  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "assets" / "icons" / "body"
W, H = 512, 768

# Slate mannequin: a mid fill, a darker edge, one flat shadow shape on the right side of the
# torso so it reads as a body and not a cut-out. Nothing here is a rarity colour.
FILL = (86, 92, 104, 255)
EDGE = (52, 56, 66, 255)
SHADE = (72, 77, 88, 255)

# Where a worn piece's icon docks, as fractions of the figure. Rings share the left hand; gloves
# take the right; the mount stands at the figure's side, off the body, larger than the rest.
DOCKS = {
    "Head":   (0.50, 0.115),
    "Neck":   (0.50, 0.235),
    "Torso":  (0.50, 0.415),
    "Gloves": (0.815, 0.605),
    "Ring1":  (0.185, 0.605),
    "Ring2":  (0.135, 0.735),
    "Boots":  (0.50, 0.925),
    "Mount":  (0.855, 0.855),
}
ORDER = ["Mount", "Boots", "Torso", "Gloves", "Ring1", "Ring2", "Neck", "Head"]


def ellipse(cx, cy, rx, ry):
    return lambda x, y: ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0


def polygon(pts):
    def inside(x, y):
        c = False
        j = len(pts) - 1
        for i in range(len(pts)):
            xi, yi = pts[i]
            xj, yj = pts[j]
            if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                c = not c
            j = i
        return c
    return inside


def rounded_rect(x0, y0, x1, y1, r):
    def inside(x, y):
        if x < x0 or x > x1 or y < y0 or y > y1:
            return False
        cx = min(max(x, x0 + r), x1 - r)
        cy = min(max(y, y0 + r), y1 - r)
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r
    return inside


def mirror(pts):
    return [(W - x, y) for x, y in pts]


def figure():
    """The shapes, in paint order. Each is (test, colour)."""
    shapes = []
    # legs, feet
    left_leg = [(190, 428), (254, 428), (248, 700), (196, 700)]
    shapes.append((polygon(left_leg), FILL))
    shapes.append((polygon(mirror(left_leg)), FILL))
    shapes.append((rounded_rect(176, 690, 262, 742, 14), FILL))
    shapes.append((rounded_rect(W - 262, 690, W - 176, 742, 14), FILL))
    # torso: shoulders to hips
    torso = [(150, 182), (362, 182), (338, 300), (326, 400), (334, 436), (178, 436), (186, 400), (174, 300)]
    shapes.append((polygon(torso), FILL))
    shapes.append((polygon([(300, 182), (362, 182), (338, 300), (326, 400), (334, 436), (296, 436), (302, 300)]), SHADE))
    # neck, head
    shapes.append((polygon([(232, 140), (280, 140), (284, 186), (228, 186)]), FILL))
    shapes.append((ellipse(256, 96, 48, 56), FILL))
    # arms: upper, forearm, hand
    upper = [(150, 186), (198, 196), (158, 334), (106, 322)]
    fore = [(106, 322), (158, 334), (138, 444), (88, 434)]
    shapes.append((polygon(upper), FILL))
    shapes.append((polygon(fore), FILL))
    shapes.append((ellipse(112, 466, 27, 29), FILL))
    shapes.append((polygon(mirror(upper)), FILL))
    shapes.append((polygon(mirror(fore)), FILL))
    shapes.append((ellipse(W - 112, 466, 27, 29), FILL))
    return shapes


def render():
    shapes = figure()
    px = [[(0, 0, 0, 0)] * W for _ in range(H)]
    for y in range(H):
        row = px[y]
        for x in range(W):
            for test, colour in shapes:
                if test(x, y):
                    row[x] = colour
    # A 3px edge: any filled pixel with a transparent neighbour within 3px goes dark.
    out = [row[:] for row in px]
    for y in range(H):
        for x in range(W):
            if px[y][x][3] == 0:
                continue
            edge = False
            for dy in (-3, -2, -1, 0, 1, 2, 3):
                yy = y + dy
                if yy < 0 or yy >= H:
                    edge = True
                    break
                for dx in (-3, -2, -1, 0, 1, 2, 3):
                    xx = x + dx
                    if xx < 0 or xx >= W or px[yy][xx][3] == 0:
                        edge = True
                        break
                if edge:
                    break
            if edge:
                out[y][x] = EDGE
    rows = []
    for y in range(H):
        line = bytearray()
        for p in out[y]:
            line.extend(p)
        rows.append(line)
    return rows


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    pnglib.write(OUT / "mannequin.png", W, H, render())
    spec = {"w": W, "h": H, "docks": {k: [round(v[0], 3), round(v[1], 3)] for k, v in DOCKS.items()}, "order": ORDER}
    io.open(OUT / "mannequin.json", "w", encoding="utf-8", newline="\n").write(json.dumps(spec, indent=2) + "\n")
    print("wrote", OUT / "mannequin.png", "and mannequin.json")


if __name__ == "__main__":
    main()
