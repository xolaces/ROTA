#!/usr/bin/env python3
"""Tags every shipped raid, and gives the four legions something to counter.

A tag answers "what IS this thing", so it is read off the raid's own fiction rather than assigned to
spread the numbers evenly. The Iron Colossus is a Wrought war-construct, so it is Construct. Malachar
commands, so he is Legion and Demon. The Drowned Custodian sits in the Sunken Vaults, so it is Horror
and Undead — the Vaults drowned with people in them.

TWO RAIDS ARE DELIBERATELY LEFT UNTAGGED. The Ashen Throne and the Cinder Crown are the campaign's
two great set-pieces, and an untagged raid is one no legion counters. That is the point: the fights
that matter most are the ones you bring your own power to, not a prepared answer. Empty tag lists are
the DEFAULT in the model for the same reason.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

# raid id -> tags, each justified by what the raid is in the fiction
RAID_TAGS = {
    # -- the two World raids ------------------------------------------------------------------
    "raid_ironcolossus":    ["Construct"],            # Order IV, the archetype Wrought
    "raid_malachar":        ["Legion", "Demon"],      # a commander, and Swollen with what he commands

    # -- chapter 1: the frontier ruins --------------------------------------------------------
    "raid_c1z1b":           ["Construct"],            # a Guardian on Old Guard ground: another Wrought
    "raid_c1z2b":           ["Undead", "Beast"],      # the Marches are too wet to bury anything

    # -- chapter 2: the burn ------------------------------------------------------------------
    "raid_c2z1b":           ["Goblin"],               # Emberfall's warrens; the Broods' cheapest work
    "raid_c2z2b":           ["Beast", "Goblin"],      # Cinderwood: the warrens and what hunts them
    "raid_c2z3b":           ["Shadow", "Construct"],  # Gloomspire — Malachar's servants, amber-lit

    # -- chapter 3: Malachar's keep -----------------------------------------------------------
    "raid_c3z0b":           ["Legion", "Construct"],  # the Keepwall garrison, and the wall itself
    "raid_c3z1b":           ["Horror", "Undead"],     # the Vaults drowned with their keepers inside
    "raid_c3z2b":           ["Demon", "Shadow"],      # a Herald on the Throne Approach
    "raid_c3z3b":           ["Construct", "Shadow"],  # the Spire shattered when a binding failed

    # -- chapter 4: the ice -------------------------------------------------------------------
    "raid_c4z0b":           ["Beast"],                # a Stalker; Order III and nothing more
    "raid_c4z1b":           ["Undead", "Horror"],     # Frostmere keeps what it takes
    "raid_c4z2b":           ["Horror"],               # a Maw is a Primordial by any other name
    "raid_c4z3b":           ["Legion", "Undead"],     # the Pale Citadel still musters its dead

    # -- chapter 5: the fire ------------------------------------------------------------------
    "raid_c5z0b":           ["Construct", "Beast"],   # a Colossus wearing Dustfall's fauna
    "raid_c5z1b":           ["Demon"],                # a Tyrant, Swollen on Emberpan's heat
    "raid_c5z2b":           ["Demon", "Shadow"],      # the Rift is where Wrath-slag cools
    "raid_c5z3b":           [],                       # THE ASHEN THRONE — untagged on purpose
    "raid_c5z4b":           [],                       # THE CINDER CROWN — untagged on purpose

    # -- chapter 6: the deep ------------------------------------------------------------------
    "raid_c6z0b":           ["Shadow", "Construct"],  # the Gate is a Wrought thing gone amber
    "raid_c6z1b":           ["Horror", "Shadow"],     # a Devourer in Star Hollow
    "raid_c6z2b":           ["Shadow"],               # the Threshold is the overlay itself
    "raid_c6z3b":           ["Dragon", "Horror"],     # the Eternal Stair is the Sovereign's own climb
    "raid_c6z4b":           ["Dragon", "Demon"],      # the Throne of Ancients, and what attends it

    # -- chapter 7: the Black Archive ---------------------------------------------------------
    "raid_lastwatch_relay": ["Construct", "Shadow"],  # relay-work still running its last order
    "raid_lastwatch_vault": ["Undead", "Shadow"],     # the Watch's own dead, keeping the vault
    "raid_lastwatch_lamp":  ["Shadow", "Horror"],     # whatever has been circling the lamp
}

# legion id -> what it was built to fight, as a PERCENT bonus. Highest-only at combat time.
# The starting legion answers nothing: a counter-build should be something you go and get.
LEGION_AFFINITIES = {
    # The Free Warband answers nothing. It is the legion you are given, and "adaptable" earning a
    # counter-bonus would make the starting kit the specialist kit.
    "legion_warband":    {},
    # The Dawn Vanguard is a marching formation. It is built to meet another formation.
    "legion_vanguard":   {"Legion": 15.0, "Goblin": 10.0},
    # The Iron Legion is type-locked into Tank/Melee/Ranged around Strength troops — a shield line,
    # which is what you bring against a thing that holds its ground and does not tire.
    "legion_ironlegion": {"Construct": 30.0, "Legion": 18.0},
    # Vanguard II is rebuilt around oathsteel, and oathsteel is what the Old Guard quenched in a
    # spoken oath. It is the anti-wrong-thing formation.
    "legion_vanguard_ii": {"Shadow": 30.0, "Undead": 20.0},
}


def main():
    rp, lp = CONTENT / "raids.json", CONTENT / "legions.json"
    raids = json.load(io.open(rp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    legions = json.load(io.open(lp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)

    tagged = 0
    for r in raids:
        if r["id"] in RAID_TAGS:
            r["tags"] = RAID_TAGS[r["id"]]
            tagged += 1
        else:
            r.setdefault("tags", [])

    touched = 0
    known = {l["id"] for l in legions}
    for l in legions:
        if l["id"] in LEGION_AFFINITIES:
            l["tagAffinities"] = LEGION_AFFINITIES[l["id"]]
            touched += 1
        else:
            l.setdefault("tagAffinities", {})

    io.open(rp, "w", encoding="utf-8", newline="\n").write(
        json.dumps(raids, indent=2, ensure_ascii=False) + "\n")
    io.open(lp, "w", encoding="utf-8", newline="\n").write(
        json.dumps(legions, indent=2, ensure_ascii=False) + "\n")

    missing = set(LEGION_AFFINITIES) - known
    counts = collections.Counter(t for r in raids for t in r.get("tags", []))
    print("raids tagged: %d/%d   legions given affinities: %d" % (tagged, len(raids), touched))
    if missing:
        print("  NOTE: affinity written for legions that do not exist yet: %s" % sorted(missing))
    print("  untagged on purpose: %s"
          % [r["id"] for r in raids if not r.get("tags")])
    print("  tag spread: %s" % dict(counts.most_common()))


if __name__ == "__main__":
    main()
