#!/usr/bin/env python3
"""Retunes the campaign raid health curve to the owner's anchors (2026-09-04).

THE REPORT. "guardian of ashen causeway has 120k hp yet in summon it said 600, I don't think
either value should hold true, the first few raids should have a few handful of thousands, and
we scale into couple million for normal raids by our final chapter, as difficulty tier scales
raid health."

Both numbers were real and both came from the same catalogue row: `baseHp` (120,000) is what a
shared summon uses and what the list shows, `personalBaseHp` (600) is the solo pool and what the
summon screen shows. The 200x gap between them is what made it look like a bug. It is not a bug —
it is two fields, both mis-tuned.

THE NEW CURVE. Geometric across the 23 chapter raids in campaign order:

    4,000  ->  2,000,000     23 raids, 1.3264 per step (+32.6%)

Difficulty multiplies on top and is untouched (Normal 1.0 / Hard 1.4 / Legendary 2.0 /
Nightmare 3.6), so the hardest fight in chapter 6 is 7,200,000 and the first raid on Nightmare is
14,400. The three Black Archive raids continue the same ratio past the campaign.

THE ONE JUDGEMENT CALL, flagged rather than buried: `personalBaseHp` was `baseHp / 200`, which
under the new curve would make the first raid a 20 HP solo pool. It is now `baseHp / 4` — a solo
fight that is clearly smaller than the group one without being nonsense. Nothing in shipped
content actually summons a Personal raid (all 104 sigils carry summonSize "Small", and the admin
direct-summon creates Large), so this is the low-stakes half of the change; it is a visible number
rather than a fought one.

DAMAGE THRESHOLDS MOVE WITH IT. The raid loot ladders were generated as FRACTIONS of baseHp
(0.05% to 40% of the Normal pool). Retuning health without them would leave every rung 30x out of
reach, so they are recomputed here from the same fractions. Nothing else in the loot tables is
touched — the reagents, the gear-on-the-last-rung fix and the Mythic tail all survive.
"""
import io
import json
import math
import collections
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

START, END = 4_000, 2_000_000
PERSONAL_DIVISOR = 4
# The same ladder the tables were built with, so the rungs keep their meaning.
FRACTIONS = [0.0005, 0.002, 0.005, 0.012, 0.03, 0.07, 0.15, 0.40]


def tidy(v):
    """Round to three significant figures so the content reads as authored, not as generated."""
    if v < 1000:
        return int(round(v, -1))
    return int(round(v, -(int(math.log10(v)) - 2)))


def main():
    rp, lp = CONTENT / "raids.json", CONTENT / "loot_tables.json"
    raids = json.load(io.open(rp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    tables = json.load(io.open(lp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)

    campaign = [r for r in raids
                if r["tier"] != "World" and not r["id"].startswith("raid_lastwatch")]
    archive = [r for r in raids if r["id"].startswith("raid_lastwatch")]

    g = (END / START) ** (1.0 / (len(campaign) - 1))
    new_hp = {}
    for i, r in enumerate(campaign):
        new_hp[r["id"]] = tidy(START * g ** i)
    for i, r in enumerate(archive):
        new_hp[r["id"]] = tidy(START * g ** (len(campaign) - 1 + i + 1))

    for r in raids:
        if r["id"] not in new_hp:
            continue                                  # World raids are timer-only, baseHp 0
        r["baseHp"] = new_hp[r["id"]]
        r["personalBaseHp"] = max(1, tidy(new_hp[r["id"]] / PERSONAL_DIVISOR))

    # Re-key every generated raid ladder to its raid's new pool. The two hand-authored tables
    # (ironcolossus / malachar) belong to World raids, which are timer-only and keyed on absolute
    # damage rather than a fraction of a pool, so they are left alone.
    moved = 0
    for t in tables:
        if t.get("type") != "Raid":
            continue
        rid = t["id"][3:]
        if rid not in new_hp:
            continue
        base = new_hp[rid]
        for diff, v in t["difficulties"].items():
            rungs = v["thresholdRewards"]
            for i, rung in enumerate(rungs):
                rung["damageThreshold"] = max(100, int(base * FRACTIONS[i]))
        moved += 1

    io.open(rp, "w", encoding="utf-8", newline="\n").write(
        json.dumps(raids, indent=2, ensure_ascii=False) + "\n")
    io.open(lp, "w", encoding="utf-8", newline="\n").write(
        json.dumps(tables, indent=2, ensure_ascii=False) + "\n")

    print("growth %.4f per raid (+%.1f%%)" % (g, (g - 1) * 100))
    print("raids retuned: %d   ladders re-keyed: %d" % (len(new_hp), moved))
    print()
    print("%-24s%>12s" % ("raid", "") if False else
          "%-24s%14s%14s%16s" % ("raid", "baseHp", "personal", "Nightmare x3.6"))
    for r in raids:
        if r["id"] in new_hp:
            print("%-24s%14s%14s%16s" % (
                r["id"], f"{r['baseHp']:,}", f"{r['personalBaseHp']:,}",
                f"{int(r['baseHp'] * 3.6):,}"))


if __name__ == "__main__":
    main()
