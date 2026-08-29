#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ONE-SHOT SEEDER for the World-raid damage ladder.

    *** Re-running OVERWRITES the two World loot tables. Needs --force. ***
    Like tools/recurve-raids.py, this exists to fill the file once so nobody types a
    hundred entries by hand. content/loot_tables.json is the source of truth afterwards.

WHAT IT BUILDS
    Owner, 2026-08-29: World raids have no collective health and are decided by a timer, with
    "raid dmg being the tier reward" on a pool "from 500 dmg to 1,000,000,000". Tiers should be
    "more often at lower tiers", thinning out until the deep millions come "only every quarter,
    such as 100,250,500,750,1,000m", with irregular values like 62,750,000 "to spice it up".

    So the ladder is dense early and sparse late: twelve rungs to clear the first million, nine
    more across the mid millions, then the five quarter-marks. Twenty-six rungs, 500 -> 1B.

REWARDS ARE CUMULATIVE, WHICH IS THE THING TO UNDERSTAND
    RaidService collects EVERY threshold the player passed, not just their highest
    (`.Where(t => ... >= t.Threshold)`). A twenty-six rung ladder therefore pays the SUM of every
    rung climbed, so each rung is deliberately small; the payout at 1B is the total, not the last
    entry. That is also why this reads as a ladder rather than a bracket: you bank each rung as
    you cross it, and nothing is lost if the timer beats you to the next one.

USAGE
    python tools/seed-world-ladder.py --dry-run
    python tools/seed-world-ladder.py --force
"""
import argparse
import io
import json
import os

# Damage rungs. Dense low, thinning through the millions, quarter-marks at the top.
# The irregular entries (62,750,000 and friends) are deliberate: a perfectly geometric ladder
# reads as generated, and the owner asked for exactly this kind of texture.
RUNGS = [
             500,        1_500,        3_000,        6_000,
          12_000,       25_000,       45_000,       80_000,
         140_000,      250_000,      425_000,      700_000,
       1_150_000,    1_850_000,    3_000_000,    4_750_000,
       7_500_000,   11_500_000,   17_500_000,   26_000_000,
      38_500_000,   62_750_000,
     100_000_000,  250_000_000,  500_000_000,  750_000_000,
   1_000_000_000,
]

# Per-rung stat points. Small, because they sum — a full climb banks the total.
def stat_points(i, n):
    if i < 8:   return 1
    if i < 14:  return 2
    if i < 19:  return 4
    if i < 23:  return 8
    return 15

# Difficulty scales the whole ladder's payout, matching how the campaign tables already work.
DIFFICULTY_MULTIPLIER = {"Normal": 1.0, "Hard": 1.4, "Legendary": 1.9, "Nightmare": 2.6}

WORLD_TABLES = ["lt_raid_ironcolossus", "lt_raid_malachar"]


def drops_for(i, n, mult):
    """Shards throughout, stat bags from the middle, a magic chance on the last three rungs."""
    out = {"itemDrops": [], "magicDrops": []}

    shards = max(1, int(round((1 + i * 0.6) * mult)))
    out["itemDrops"].append({
        "itemId": "mat_iron_shard", "quantity": shards,
        "chance": round(min(1.0, 0.45 + i * 0.03), 2),
    })

    if i >= 10:
        out["itemDrops"].append({
            "itemId": "statbag_minor", "quantity": 1,
            "chance": round(min(0.9, 0.15 + (i - 10) * 0.04), 2),
        })
    if i >= 17:
        out["itemDrops"].append({
            "itemId": "statbag_major", "quantity": 1,
            "chance": round(min(0.75, 0.10 + (i - 17) * 0.05), 2),
        })
    if i >= n - 3:
        out["magicDrops"].append({
            "magicId": "magic_impending_doom",
            "chance": round(0.02 + (i - (n - 3)) * 0.03, 2),
        })

    if not out["magicDrops"]:
        del out["magicDrops"]
    return out


def build_difficulty(difficulty):
    mult = DIFFICULTY_MULTIPLIER[difficulty]
    n = len(RUNGS)
    rewards = []
    for i, dmg in enumerate(RUNGS):
        r = {
            # Absolute damage, NOT a share of the total. A timer-only raid has no total to take a
            # share of until it ends, and a ladder you can see is worth more than one you cannot.
            "damageThreshold": dmg,
            "contributionPercent": 0.0,
            "unassignedStatPoints": max(1, int(round(stat_points(i, n) * mult))),
            "attackPoints": 1 if i >= 19 else 0,
            "defensePoints": 1 if i >= 22 else 0,
            "discernmentPoints": 1 if i >= 24 else 0,
        }
        r.update(drops_for(i, n, mult))
        rewards.append(r)
    return {"minContributionPercent": 0.0, "onHitDrops": None, "thresholdRewards": rewards}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--content", default=os.path.join("src", "ROTA.Api", "content", "loot_tables.json"))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    if not args.dry_run and not args.force:
        raise SystemExit(
            "Refusing to run.\n\n"
            "loot_tables.json is hand-authored. Re-seeding would overwrite the two World ladders.\n\n"
            "  --dry-run   print the ladder, change nothing\n"
            "  --force     re-seed anyway")

    with io.open(args.content, encoding="utf-8-sig") as h:
        tables = json.load(h)

    n = len(RUNGS)
    print("%-4s %16s %8s %s" % ("rung", "damage", "SP(Nor)", "drops"))
    print("-" * 78)
    total_sp = 0
    for i, dmg in enumerate(RUNGS):
        sp = stat_points(i, n)
        total_sp += sp
        d = drops_for(i, n, 1.0)
        bits = ["%dx %s @%.0f%%" % (x["quantity"], x["itemId"].replace("mat_", "").replace("statbag_", "bag:"),
                                    x["chance"] * 100) for x in d["itemDrops"]]
        if "magicDrops" in d:
            bits.append("magic @%.0f%%" % (d["magicDrops"][0]["chance"] * 100))
        print("%-4d %16s %8d  %s" % (i + 1, format(dmg, ","), sp, "  ".join(bits)))

    print("\n%d rungs, 500 -> %s." % (n, format(RUNGS[-1], ",")))
    print("A full climb banks %d skill points on Normal (rewards are CUMULATIVE), "
          "%d on Nightmare." % (total_sp, round(total_sp * DIFFICULTY_MULTIPLIER["Nightmare"])))

    touched = 0
    for t in tables:
        if t["id"] in WORLD_TABLES:
            t["difficulties"] = {d: build_difficulty(d) for d in DIFFICULTY_MULTIPLIER}
            touched += 1
    if touched != len(WORLD_TABLES):
        raise SystemExit("Expected %d World loot tables, found %d." % (len(WORLD_TABLES), touched))

    if args.dry_run:
        print("\n(dry run -- nothing written)")
        return

    with io.open(args.content, "w", encoding="utf-8", newline="\n") as h:
        json.dump(tables, h, indent=2, ensure_ascii=False)
        h.write("\n")
    print("\nWrote %s (%d World tables)" % (args.content, touched))


if __name__ == "__main__":
    main()
