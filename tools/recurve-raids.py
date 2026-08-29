#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ONE-SHOT SEEDER for raid health. Already run on 2026-08-29 — content/raids.json is now the
source of truth and is meant to be edited BY HAND.

    *** Re-running this OVERWRITES every hand-typed health value and name. ***
    It refuses to run without --force for exactly that reason.

WHY IT IS RETIRED
    Owner, 2026-08-29: "Can we make raid health based off a .json data file? where we just input
    the health rather than grade it? would make for adding a lot simpler."

    Right call, and raids.json already worked that way — `baseHp` has always been a plain number.
    This script was computing it, which put a formula between the designer and the number. It ran
    once to seed a sensible ladder so nobody had to type twenty-three values from scratch; from
    here, adding a raid is: copy an entry, change the id, name, grade and health. Nothing derives.

    `grade` (Common | Deadly | Elite | Mythic) survives as a LABEL only. It drives display, not
    health. Keeping it out of the raid's name means re-theming later is a find-replace rather than
    a rewrite.

WHAT PROTECTS THE NUMBERS NOW
    RaidDefinitionProvider.Validate fails the boot on a raid with no health, a negative personal
    health, an unknown grade, a missing name, a duplicate id, or a zero timer — and on a World raid
    that HAS health, since World raids are decided by a timer and a damage ladder. Hand-typing is
    only safe when a typo stops the server instead of shipping.

HOW THE SEEDED NUMBERS WERE DERIVED (kept for reference, not re-run)
    An endgame player deals roughly 600k damage per 20-stamina hit (EffectiveAttack x 4 + Defense,
    x hitSize, x the RNG band, with legion and gear folded in). The top raid at 560M is therefore
    about 930 hits — 90 to 190 hits each across five to ten players inside the 48h window, which is
    what "goaled for mass hitters" means in numbers. personalBaseHp is baseHp / 200, the ratio the
    original content already used.

USAGE
    python tools/recurve-raids.py --dry-run   # print the table, touch nothing
    python tools/recurve-raids.py --force     # re-seed, DESTROYING hand edits
"""
import argparse
import io
import json
import os

# Grade multipliers. Retuning the ladder starts here.
GRADE_MULTIPLIER = {
    "Common": 1.0,
    "Deadly": 2.5,
    "Elite":  6.0,
    "Mythic": 14.0,
}

# Campaign-ordered plan: (new name, grade, campaign step).
# The step drives the base curve; the grade multiplies it. Names are PLACEHOLDERS chosen to break
# the single-theme problem -- "Guardian of X" is kept on 7 of 23 (30%), and the rest vary in form
# (Warden / Herald / The X / X-breaker) so the roster reads as a world rather than a list.
PLAN = [
    # id            new name                              grade     step
    ("raid_c1z1b", "Guardian of Ashen Causeway",          "Common",  1),
    ("raid_c1z2b", "The Hollow Marcher",                  "Common",  2),
    ("raid_c2z1b", "Warden of Emberfall",                 "Common",  3),
    ("raid_c2z2b", "Guardian of Cinderwood",              "Common",  4),
    ("raid_c2z3b", "The Gloomspire Sentinel",             "Deadly",  5),
    ("raid_c3z0b", "Guardian of Keepwall",                "Deadly",  6),
    ("raid_c3z1b", "The Drowned Custodian",               "Deadly",  7),
    ("raid_c3z2b", "Herald of the Approach",              "Deadly",  8),
    ("raid_c3z3b", "Spirebreaker",                        "Deadly",  9),
    ("raid_c4z0b", "The Rimewood Stalker",                "Elite",  10),
    ("raid_c4z1b", "Guardian of Frostmere",               "Elite",  11),
    ("raid_c4z2b", "Maw of the Glacier",                  "Elite",  12),
    ("raid_c4z3b", "Warden of the Pale Citadel",          "Elite",  13),
    ("raid_c5z0b", "The Dustfall Colossus",               "Elite",  14),
    ("raid_c5z1b", "Tyrant of Emberpan",                  "Elite",  15),
    ("raid_c5z2b", "Guardian of Magma Rift",              "Elite",  16),
    ("raid_c5z3b", "The Ashen Throne",                    "Mythic", 17),
    ("raid_c5z4b", "The Cinder Crown",                    "Mythic", 18),
    ("raid_c6z0b", "Guardian of Twilight Gate",           "Mythic", 19),
    ("raid_c6z1b", "Devourer of Star Hollow",             "Mythic", 20),
    ("raid_c6z2b", "The Void Threshold",                  "Mythic", 21),
    ("raid_c6z3b", "Warden of the Eternal Stair",         "Mythic", 22),
    ("raid_c6z4b", "Guardian of the Throne of Ancients",  "Mythic", 23),
]

# Base curve before the grade multiplier: geometric from FIRST to LAST across the campaign steps.
FIRST_STEP_HP = 120_000
LAST_STEP_HP  = 40_000_000        # x14 (Mythic) lands the final raid at ~560M
PERSONAL_DIVISOR = 200


def base_for_step(step, steps):
    """Geometric interpolation, so each step is a constant RATIO harder than the last."""
    if steps <= 1:
        return FIRST_STEP_HP
    ratio = (LAST_STEP_HP / FIRST_STEP_HP) ** (1.0 / (steps - 1))
    return FIRST_STEP_HP * (ratio ** (step - 1))


def round_sig(v, digits=3):
    """Content numbers a designer reads, not floating-point noise."""
    if v <= 0:
        return 0
    from math import floor, log10
    mag = floor(log10(v))
    factor = 10 ** (mag - digits + 1)
    return int(round(v / factor) * factor)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--content", default=os.path.join("src", "ROTA.Api", "content", "raids.json"))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--force", action="store_true",
                    help="Re-seed, overwriting hand-typed health and names.")
    args = ap.parse_args()

    if not args.dry_run and not args.force:
        raise SystemExit(
            "Refusing to run.\n\n"
            "raids.json is hand-authored now - health is typed directly, not generated.\n"
            "Re-seeding would overwrite every value and name in it.\n\n"
            "  --dry-run   print the table, change nothing\n"
            "  --force     re-seed anyway, destroying hand edits")

    with io.open(args.content, encoding="utf-8-sig") as h:
        raids = json.load(h)
    by_id = {r["id"]: r for r in raids}

    steps = len(PLAN)
    print("%-34s %-8s %14s %12s" % ("name", "grade", "baseHp", "personalHp"))
    print("-" * 74)

    for raid_id, name, grade, step in PLAN:
        r = by_id.get(raid_id)
        if r is None:
            raise SystemExit("raids.json has no raid '%s' -- the plan is stale." % raid_id)

        base = round_sig(base_for_step(step, steps) * GRADE_MULTIPLIER[grade])
        r["name"] = name
        r["grade"] = grade
        r["baseHp"] = base
        r["personalBaseHp"] = round_sig(base / PERSONAL_DIVISOR)

        print("%-34s %-8s %14s %12s" % (name, grade, format(base, ","), format(r["personalBaseHp"], ",")))

    # ── World raids: no collective health, seven-day timer ────────────────────
    # Owner 2026-08-29: "World raids will have no collective health and solely be a timer, with raid
    # dmg being the tier reward." baseHp 0 is the marker for that; the reward ladder is absolute
    # damage and lives in loot_tables.json, not here.
    print()
    for r in raids:
        if r.get("tier") == "World":
            r["grade"] = "Mythic"
            r["baseHp"] = 0
            r["personalBaseHp"] = 0
            r["timerHours"] = 168
            print("%-34s %-8s %14s   timer 168h (7d), damage-ladder rewards"
                  % (r["name"], r["grade"], "no health"))

    kept = sum(1 for _, n, _, _ in PLAN if n.startswith("Guardian of"))
    print("\n%d of %d campaign raids keep the Guardian name (%.0f%%)."
          % (kept, steps, kept * 100.0 / steps))

    if args.dry_run:
        print("\n(dry run -- nothing written)")
        return

    with io.open(args.content, "w", encoding="utf-8", newline="\n") as h:
        json.dump(raids, h, indent=2, ensure_ascii=False)
        h.write("\n")
    print("\nWrote %s" % args.content)


if __name__ == "__main__":
    main()
