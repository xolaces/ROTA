#!/usr/bin/env python3
"""Makes wave 2 obtainable. Content nothing drops is worse than no content — it is a promise the
game does not keep, and it costs a player their time to discover.

THREE WIRINGS, EACH WITH ITS OWN RULE.

1. TAG REAGENTS FOLLOW THEIR TAG, PROGRAMMATICALLY. A reagent carrying tags ["Goblin"] is added to
   the loot table of every raid carrying "Goblin". The mapping is derived, not hand-listed, so a raid
   retagged later automatically drops the right things and cannot silently drift out of step with its
   own fiction. This is the payoff for tagging raids from their lore rather than by feel.

2. NEW GEAR SETS DROP WHERE THEIR FICTION PUTS THEM. The Warrens Kit is Iron Weir goblin work, so it
   falls in the chapter-2 warrens; Gravewarden is Last Watch grave-work, so the Marches and the ice;
   Wroughtbreaker is siege gear, so the deep chapters. Each set is spread across its chapter's zones
   so no single node is the whole set.

3. THE TWO ORANGE SETS ARE CHASE, NOT FARM. Sovereign's Regalia and The Stoned Devil sit at
   rareScaling rates in the 0.05%-0.15% band, on the deepest content only. A player can finish the
   campaign having seen neither, which is what an Orange set should mean.

QUEST GEAR HONOURS ITS CHANCE; RAID GEAR DOES NOT. RaidService grants threshold gear unconditionally
and cumulatively (owner decision 0e), so every raid-side gear entry here goes on the LAST rung only
and at chance 1.0 — the JSON says what the engine does. Reagents on the raid side DO roll, because
ItemDrops are chance-rolled on that path.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

DIFFS = ("Normal", "Hard", "Legendary", "Nightmare")
QTY_MULT = {"Normal": 1.0, "Hard": 1.5, "Legendary": 2.0, "Nightmare": 3.0}
CHANCE_MULT = {"Normal": 1.0, "Hard": 1.15, "Legendary": 1.3, "Nightmare": 1.5}

# set -> (chapters it drops in, per-piece chance at Normal, rareScaling)
SET_HOMES = {
    "set_warrens":        ([2],       0.028, True),
    "set_marchwatch":     ([1, 2],    0.028, True),
    "set_gravewarden":    ([3, 4],    0.020, True),
    "set_drowned":        ([3],       0.022, True),
    "set_wroughtbreaker": ([5],       0.013, True),
    "set_choir":          ([6],       0.013, True),
    "set_sovereign":      ([6, 7],    0.0012, True),   # chase
    "set_stoned_devil":   ([5, 6],    0.0010, True),   # chase, and deliberately not where you look
}


def scaled(chance, diff):
    return round(min(0.95, chance * CHANCE_MULT[diff]), 6)


def qty(base, diff):
    return max(1, int(round(base * QTY_MULT[diff])))


def main():
    gear = json.load(io.open(CONTENT / "gear.json", encoding="utf-8"))
    items = json.load(io.open(CONTENT / "items.json", encoding="utf-8"))
    quests = json.load(io.open(CONTENT / "quests.json", encoding="utf-8"))
    raids = json.load(io.open(CONTENT / "raids.json", encoding="utf-8"))
    lp = CONTENT / "loot_tables.json"
    tables = json.load(io.open(lp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    by_table = {t["id"]: t for t in tables}

    # ── 1. tag reagents -> every raid carrying that tag ───────────────────────────────────────
    tag_reagents = collections.defaultdict(list)
    for it in items:
        for tag in (it.get("tags") or []):
            tag_reagents[tag].append(it["id"])

    reagent_adds = 0
    for r in raids:
        tid = r.get("lootTableId")
        if not tid or tid not in by_table:
            continue
        wanted = sorted({rid for tag in r.get("tags", []) for rid in tag_reagents.get(tag, [])})
        if not wanted:
            continue
        t = by_table[tid]
        for diff, block in (t.get("difficulties") or {}).items():
            rungs = block.get("thresholdRewards") or []
            if not rungs:
                continue
            # Mid-ladder, so a tag reagent is a reward for fighting the thing properly rather than
            # for turning up. Rung 4 of 8 is the halfway mark on the shipped ladders.
            target = rungs[len(rungs) // 2]
            have = {d["itemId"] for d in target.get("itemDrops", [])}
            for rid in wanted:
                if rid in have:
                    continue
                target.setdefault("itemDrops", []).append({
                    "itemId": rid, "quantity": qty(1, diff), "chance": scaled(0.22, diff),
                })
                reagent_adds += 1

    # ── 2. new gear sets -> the quest zones their fiction puts them in ────────────────────────
    set_pieces = collections.defaultdict(list)
    for g in gear:
        if g.get("setId") in SET_HOMES:
            set_pieces[g["setId"]].append(g["id"])

    zones_by_chapter = collections.defaultdict(list)
    for q in quests:
        key = (q["chapter"], q["zoneIndex"])
        if key not in zones_by_chapter[q["chapter"]]:
            zones_by_chapter[q["chapter"]].append(key)

    gear_adds = 0
    for set_id, (chapters, chance, rare) in SET_HOMES.items():
        pieces = sorted(set_pieces.get(set_id, []))
        if not pieces:
            continue
        homes = [z for ch in chapters for z in zones_by_chapter.get(ch, [])]
        if not homes:
            continue
        # Spread the set across its chapters' zones so no single node is the whole set.
        for i, piece in enumerate(pieces):
            ch, zi = homes[i % len(homes)]
            for suffix in ("", "b"):                       # the zone table and its boss table
                tid = "lt_zone_c%dz%d%s" % (ch, zi, suffix)
                t = by_table.get(tid)
                if t is None:
                    continue
                for diff, block in (t.get("difficulties") or {}).items():
                    drops = block.setdefault("gearDrops", [])
                    if any(d["gearDefinitionId"] == piece for d in drops):
                        continue
                    boss_mult = 2.0 if suffix == "b" else 1.0
                    drops.append({
                        "gearDefinitionId": piece, "quantity": 1,
                        "chance": scaled(chance * boss_mult, diff), "rareScaling": rare,
                    })
                    gear_adds += 1

    io.open(lp, "w", encoding="utf-8", newline="\n").write(
        json.dumps(tables, indent=2, ensure_ascii=False) + "\n")

    print("tag-reagent drop entries added: %d" % reagent_adds)
    print("gear drop entries added:        %d" % gear_adds)
    print("  tags with a reagent behind them: %s" % dict(sorted(
        (k, len(v)) for k, v in tag_reagents.items())))


if __name__ == "__main__":
    main()
