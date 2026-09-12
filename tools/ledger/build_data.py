"""Builds out/ledger/loot_data.json for the Loot Ledger page from the shipped content + icons.

Run `python tools/ledger/build.py` instead; it calls this and then assembles the page."""
import base64
import collections
import io
import json
import pathlib
import re
import sys
import zlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[2]
CONTENT = ROOT / "src" / "ROTA.Api" / "content"
ICONS = ROOT / "assets" / "icons"
OUT = ROOT / "out" / "ledger" / "loot_data.json"
sys.path.insert(0, str(ROOT / "tools" / "art"))
import build_atlas  # noqa: E402  (_read_png / _resample)

# Mirrors tools/content/reforge_sets.py — the display reads the same constants.
sys.path.insert(0, str(ROOT / "tools" / "content"))
import reforge_sets as rs  # noqa: E402


def load(name):
    return json.load(io.open(CONTENT / name, encoding="utf-8"))


def png_bytes(width, height, rows):
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        raw.extend(rows[y])

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


_icon_cache = {}


def box_down(rows, w, h, size):
    """Alpha-weighted box filter to a square: real art is 512px and nearest-neighbour at 64px
    turns its edges to sand."""
    out = []
    for y in range(size):
        y0, y1 = y * h // size, max(y * h // size + 1, (y + 1) * h // size)
        line = bytearray(size * 4)
        for x in range(size):
            x0, x1 = x * w // size, max(x * w // size + 1, (x + 1) * w // size)
            r = g = b = a = 0
            n = 0
            for yy in range(y0, y1):
                row = rows[yy]
                for xx in range(x0, x1):
                    o = xx * 4
                    al = row[o + 3]
                    r += row[o] * al; g += row[o + 1] * al; b += row[o + 2] * al; a += al
                    n += 1
            if a:
                line[x * 4:x * 4 + 4] = bytes((r // a, g // a, b // a, a // n))
        out.append(line)
    return out


def icon_uri(family, stem, size=64):
    key = (family, stem, size)
    if key in _icon_cache:
        return _icon_cache[key]
    p = ICONS / family / (stem + ".png")
    if not p.exists():
        _icon_cache[key] = None
        return None
    w, h, rows = build_atlas._read_png(p)
    px = box_down(rows, w, h, size) if w >= 2 * size else build_atlas._resample(rows, w, h, size)
    uri = "data:image/png;base64," + base64.b64encode(png_bytes(size, size, px)).decode("ascii")
    _icon_cache[key] = uri
    return uri


gear = load("gear.json")
items = load("items.json")
recipes = load("recipes.json")
tables = {t["id"]: t for t in load("loot_tables.json")}
quests = load("quests.json")
raids = load("raids.json") + load("guild_raids.json")
gear_by_id = {g["id"]: g for g in gear}
item_by_id = {i["id"]: i for i in items}
raid_by_id = {r["id"]: r for r in raids}

RARITY_ORDER = ["Grey", "White", "Green", "Blue", "Purple", "Orange"]
SLOT_ORDER = ["Head", "Neck", "Torso", "Gloves", "Boots", "Ring1", "Ring2", "Mount"]

# ── sets
sets = []
for set_id, (name, scrap_blurb, tack_blurb) in rs.SETS.items():
    pieces = [g for g in gear if g.get("setId") == set_id]
    pieces.sort(key=lambda g: SLOT_ORDER.index(g["slot"]))
    rarity = pieces[0]["rarity"]
    rows = []
    for g in pieces:
        r = gear_by_id[g["id"] + "_reforged"]
        recipe = next(x for x in recipes if x["outputId"] == r["id"])
        part = next(i for i in recipe["ingredients"] if i["kind"] == "Item")
        stem = pathlib.PurePosixPath(g["iconPath"]).stem
        rows.append({
            "id": g["id"], "name": g["name"], "slot": g["slot"],
            "atk": g["bonusAttack"], "def": g["bonusDefense"],
            "ratk": r["bonusAttack"], "rdef": r["bonusDefense"],
            "partId": part["id"], "partQty": part["quantity"], "gold": recipe["goldCost"],
            "icon": icon_uri("gear", stem),
        })
    sets.append({
        "id": set_id, "name": name, "rarity": rarity, "pieces": rows,
        "scrap": {"id": rs.scrap_id(set_id), "name": item_by_id[rs.scrap_id(set_id)]["name"],
                  "blurb": scrap_blurb, "icon": icon_uri("item", rs.scrap_id(set_id))},
        "tack": {"id": rs.tack_id(set_id), "name": item_by_id[rs.tack_id(set_id)]["name"],
                 "blurb": tack_blurb, "icon": icon_uri("item", rs.tack_id(set_id))},
    })
sets.sort(key=lambda s: (RARITY_ORDER.index(s["rarity"]), s["name"]))

# ── quest pools: every (zone, table) a node uses, with the node count behind it
zones = collections.OrderedDict()
for q in quests:
    key = (q["chapter"], q["zoneIndex"])
    z = zones.setdefault(key, {"chapter": q["chapter"], "zoneIndex": q["zoneIndex"], "name": q["zoneName"],
                               "nodes": 0, "energy": [], "tables": collections.OrderedDict()})
    z["nodes"] += 1
    z["energy"].append(q["baseEnergyCost"])
    tid = q.get("lootTableId")
    if tid and tid in tables:
        t = z["tables"].setdefault(tid, {"table": tid, "nodes": 0, "boss": q["nodeType"] == "Boss",
                                         "energy": []})
        t["nodes"] += 1
        t["energy"].append(q["baseEnergyCost"])


def pool_of(table_id):
    t = tables[table_id]
    pool = []
    for d in t["difficulties"]["Normal"].get("gearDrops") or []:
        g = gear_by_id[d["gearDefinitionId"]]
        chances = {diff: next(x["chance"] for x in t["difficulties"][diff]["gearDrops"]
                              if x["gearDefinitionId"] == d["gearDefinitionId"])
                   for diff in rs.DIFFICULTIES}
        pool.append({"id": g["id"], "name": g["name"], "set": g.get("setId"), "rarity": g["rarity"],
                     "slot": g["slot"], "chance": chances,
                     "tail": g.get("setId") is None and d["chance"] < 0.001,
                     "icon": icon_uri("gear", pathlib.PurePosixPath(g["iconPath"]).stem, 40)})
    return pool


zone_rows = []
for key, z in sorted(zones.items()):
    pools = []
    for tid, t in z["tables"].items():
        pool = pool_of(tid)
        if not pool:
            continue
        pools.append({"table": tid, "nodes": t["nodes"], "boss": t["boss"],
                      "energy": round(sum(t["energy"]) / len(t["energy"]), 1), "pool": pool})
    if not pools:
        continue
    zone_rows.append({"chapter": z["chapter"], "zone": z["zoneIndex"], "name": z["name"],
                      "nodes": z["nodes"], "pools": pools})

# ── raids: ladders with parts
raid_rows = []
grade_order = {"Common": 0, "Deadly": 1, "Elite": 2, "Mythic": 3, None: 4}
for r in raids:
    tid = r.get("lootTableId")
    if not tid or tid not in tables:
        continue
    t = tables[tid]
    m = re.match(r"raid_c(\d+)z(\d+)b", r["id"])
    # The Black Archive trio has no c7 id but is the chapter-7 boss line (quests.json's zone c7z0).
    chapter = int(m.group(1)) if m else (7 if r["id"].startswith("raid_lastwatch_") else None)
    sets_here = rs.RAID_SETS.get(r["id"], [])
    diffs = {}
    for diff in rs.DIFFICULTIES:
        rungs = t["difficulties"][diff]["thresholdRewards"]
        n = len(rungs)
        out = []
        for i, rung in enumerate(rungs):
            drops = rung.get("itemDrops") or []
            parts = [(d["itemId"], d["chance"]) for d in drops if d["itemId"].endswith(("_scrap", "_tack"))]
            reagents = [(d["itemId"], d["quantity"], d["chance"]) for d in drops
                        if not d["itemId"].endswith(("_scrap", "_tack"))]
            out.append({
                "threshold": rung["damageThreshold"], "sp": rung["unassignedStatPoints"],
                "reagents": [{"id": a, "name": item_by_id[a]["name"], "qty": b, "chance": c} for a, b, c in reagents],
                "gear": [x["gearDefinitionId"] for x in rung.get("gearDrops") or []],
                "magic": [(x["magicId"], x["chance"]) for x in rung.get("magicDrops") or []],
            })
        scrap_q = next((d["chance"] for rung in rungs for d in rung.get("itemDrops") or [] if d["itemId"].endswith("_scrap")), 0)
        tack_q = next((d["chance"] for rung in rungs for d in rung.get("itemDrops") or [] if d["itemId"].endswith("_tack")), 0)
        diffs[diff] = {"rungs": out, "scrapQ": scrap_q, "tackQ": tack_q, "n": n}
    raid_rows.append({
        "id": r["id"], "name": r["name"], "tier": r.get("tier"), "grade": r.get("grade"),
        "chapter": chapter, "baseHp": r.get("baseHp", 0), "timerHours": r.get("timerHours"),
        "tags": r.get("tags") or [], "sets": sets_here,
        "icon": icon_uri("raid", r["id"], 48),
        "difficulties": diffs,
    })
raid_rows.sort(key=lambda x: ({"Standard": 0, "Guild": 1, "World": 2}.get(x["tier"], 3), x["chapter"] or 99, x["baseHp"]))

hp_mult = {"Normal": 1.0, "Hard": 1.4, "Legendary": 2.0, "Nightmare": 3.6}

data = {
    "generated": "2026-09-11",
    "constants": {
        "scrapTop": rs.SCRAP_TOP, "tackTop": rs.TACK_TOP, "reforgeMult": rs.REFORGE_MULT,
        "scrapsPerPiece": rs.SCRAPS_PER_PIECE, "tackPerMount": rs.TACK_PER_MOUNT,
        "gold": rs.GOLD, "mountGoldMult": rs.MOUNT_GOLD_MULT,
        "questRate": {str(k): v for k, v in rs.QUEST_GEAR_RATE.items()},
        "questDiff": rs.QUEST_DIFF, "bossMult": rs.BOSS_NODE_MULT,
        "hpMult": hp_mult,
        "tierMult": {"Legendary1": 1.5, "Legendary2": 1.25, "Legendary3": 1.1, "Epic": 1.0, "Rare": 0.75, "Participant": 0.25},
    },
    "sets": sets,
    "zones": zone_rows,
    "raids": raid_rows,
    "counts": {"gear": len(gear), "reforged": sum(1 for g in gear if g["id"].endswith("_reforged")),
               "recipes": sum(1 for r in recipes if r["category"] == "Reforge"),
               "raidsWithParts": len(rs.RAID_SETS), "zones": len(zone_rows)},
}
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
print("wrote", OUT, OUT.stat().st_size // 1024, "KB;", len(sets), "sets", len(zone_rows), "zones", len(raid_rows), "raids")
