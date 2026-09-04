#!/usr/bin/env python3
"""Gives every quest zone and every raid something to drop, and says why.

THE HOLE THIS FILLS. 134 of the 139 quest nodes carried lootTableId: null, and 26 of the
28 raids carried lootTableId: "". Outside the five legacy q001-q005 nodes and two raids,
the entire campaign paid gold and XP and nothing else. A player could clear six chapters
and own no item that came off the ground.

TWO LINES, NEVER CROSSING. Road reagents drop from quests (paid for in ENERGY) and Field
reagents drop from raid damage thresholds (paid for in STAMINA). Nothing in this file
puts a Field reagent in a quest table or a Road reagent in a raid table, and the capstone
recipes need one of each. That is what stops a single-pool build from finishing a set.

WHERE A THING DROPS IS AN ARGUMENT, NOT A ROLL. Each zone drops the material its ground is
actually made of: Causeway Ash on the Ashen Causeway, Rime-Glass out of Frostmere,
Cinder-Salt off the Ashen Throne. The gear tier follows the faction whose territory it is
-- Iron Weir issue in the frontier chapters, Last Watch relay kit through the keeps and
the ice, House Sable Vein in the deep chapters.

THE TAIL. Every deep relic has exactly ONE home, chosen because the lore puts it there,
and a rate between 0.1% and 0.02%. They are Discernment-scaled (rareScaling), so the stat
is what moves them, and even a heavily invested player is looking at a long campaign of
reruns. The Haft of Gravewend is the deliberate oddity: it sits in the Hollow Marches, a
chapter-1 zone, at 0.02% -- the cheapest ground in the game holding the longest odds,
because that is where the man with the pitchfork actually lived.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

# Difficulty multipliers on quantity, mirroring the shipped lt_quest_q003 shape (2/3/4/6).
QTY = {"Normal": 1.0, "Hard": 1.5, "Legendary": 2.0, "Nightmare": 3.0}
# Chance multipliers. Nightmare is the best place to farm, which is the point of pushing.
CHANCE = {"Normal": 1.0, "Hard": 1.15, "Legendary": 1.3, "Nightmare": 1.5}

# ── The road: (chapter, zone) -> the reagent that ground is made of, and its base quantity.
ZONE_REAGENT = {
    (1, 0): ("mat_iron_shard", 2),        # Old Guard Ruins -- the Colossus sheds it
    (1, 1): ("mat_causeway_ash", 3),      # Ashen Causeway
    (1, 2): ("mat_hollow_reed", 3),       # Hollow Marches
    (2, 0): ("mat_vanguard_banner", 1),   # Vanguard Approach -- Pano's line stood here
    (2, 1): ("mat_emberfall_slag", 2),    # Emberfall Reach
    (2, 2): ("mat_emberfall_slag", 2),    # Cinderwood -- same fire, further out
    (2, 3): ("mat_arcane_dust", 2),       # Gloomspire -- Malachar's servants
    (3, 0): ("mat_keepwall_mortar", 2),   # Keepwall
    (3, 1): ("mat_oathsteel", 1),         # Sunken Vaults -- what the Vaults were built to hold
    (3, 2): ("mat_keepwall_mortar", 2),   # Throne Approach
    (3, 3): ("mat_arcane_dust", 3),       # Shattered Spire
    (4, 0): ("mat_rime_glass", 1),        # Rimewood
    (4, 1): ("mat_rime_glass", 2),        # Frostmere -- the source
    (4, 2): ("mat_rime_glass", 2),        # Glacier Maw
    (4, 3): ("mat_oathsteel", 2),         # Pale Citadel
    (5, 0): ("mat_cinder_salt", 1),       # Dustfall
    (5, 1): ("mat_cinder_salt", 1),       # Emberpan
    (5, 2): ("mat_cinder_salt", 2),       # Magma Rift
    (5, 3): ("mat_cinder_salt", 2),       # Ashen Throne -- the source
    (5, 4): ("mat_cinder_salt", 3),       # Cinder Crown
    (6, 0): ("mat_starhollow_dust", 1),   # Twilight Gate
    (6, 1): ("mat_starhollow_dust", 2),   # Star Hollow -- the source
    (6, 2): ("mat_starhollow_dust", 2),   # Void Threshold
    (6, 3): ("mat_starhollow_dust", 3),   # Eternal Stair
    (6, 4): ("mat_starhollow_dust", 3),   # Throne of Ancients
    (7, 0): ("mat_quiet_lamp_oil", 1),    # Black Archive -- the lamps are the archive
}

# ── The gear a chapter's ground gives up, at the rates a chapter-long grind implies.
#    Chance is per attempt at Normal; difficulty scales it. Roughly: a full tier set is
#    tens of clears at the common rungs, hundreds at the mount.
CHAPTER_GEAR = {
    1: [("gear_weir_kettle_helm", 0.030), ("gear_weir_gorget", 0.030),
        ("gear_weir_brigandine", 0.025), ("gear_weir_marchboots", 0.030)],
    2: [("gear_causeway_seal", 0.028), ("gear_marchwarden_band", 0.028),
        ("gear_weir_handguards", 0.030), ("gear_weir_courser", 0.008)],
    3: [("gear_relay_hood", 0.022), ("gear_relay_torc", 0.022),
        ("gear_relay_coat", 0.018), ("gear_relay_treads", 0.022)],
    4: [("gear_lamp_seal", 0.020), ("gear_vaultkeeper_band", 0.020),
        ("gear_relay_grips", 0.022), ("gear_relay_charger", 0.006)],
    5: [("gear_sable_circlet", 0.014), ("gear_sable_collar", 0.014),
        ("gear_sable_mantle", 0.011), ("gear_sable_treads", 0.014)],
    6: [("gear_sable_seal", 0.012), ("gear_sable_band", 0.012),
        ("gear_sable_grips", 0.014), ("gear_sable_courser", 0.004)],
    7: [("gear_sable_seal", 0.020), ("gear_sable_courser", 0.008)],
}

# ── The tail. (chapter, zone) -> [(kind, id, chance, why)]. One home each, and the reason
#    is the entry's fourth field so it cannot drift away from the table it justifies.
DEEP_DROPS = {
    (1, 0): [("gear", "gear_colossus_core", 0.0002,
              "pried from the Iron Colossus, which is the boss of this zone")],
    (1, 2): [("item", "mat_gravewend_haft", 0.0002,
              "the Heartmarch village the pitchfork came from was in the Marches")],
    (2, 0): [("gear", "gear_cold_token", 0.0003,
              "Pano's Vanguard marched from here and did not come back")],
    (3, 1): [("item", "mat_sounding_horn", 0.0005,
              "the Vaults are drowned, and the Leviathan is what drowned things answer to"),
             ("gear", "gear_sealwright_stylus", 0.0003,
              "null-sigil work sealed the Vaults; a Sealwright was here")],
    (3, 3): [("item", "mat_null_sigil_ink", 0.0005,
              "the Spire shattered because a binding failed, and the ink survived the binding")],
    (5, 4): [("gear", "gear_cinder_cuff", 0.0005,
              "Wrath-slag cools where a Manifestation stood, and one stood on the Cinder Crown")],
    (6, 4): [("gear", "gear_unworn_crown", 0.0002,
              "a circlet made for a head no carving depicts, at the throne of the things "
              "no carving depicts")],
    (7, 0): [("item", "mat_null_sigil_ink", 0.0010,
              "the Archive is where the Sealwrights wrote"),
             ("item", "mat_quiet_lamp_oil", 0.0010,
              "drawn from the relay lamps the Archive exists to keep lit")],
}

# ── The field: raid grade -> the reagent that comes off that kind of kill.
GRADE_REAGENT = {
    "Common": "mat_mire_ichor",
    "Elite":  "mat_brood_chitin",
    "Deadly": "mat_stag_tendon",
    "Mythic": "mat_wrathslag",
}
# Raid-only gear. ONE piece, on the TOP rung only, and never Orange.
#
# RaidService grants threshold gear UNCONDITIONALLY -- "no chance roll ... a GUARANTEED drop" --
# and threshold rewards are CUMULATIVE, so a player banks every rung they passed. Gear spread over
# four rungs with a chance field attached is therefore four guaranteed copies per clear, and the
# chance is not read at all. The two raid tables that shipped before this file carry no gear for
# exactly that reason. One piece on the last rung is the most this semantic can express honestly:
# top the raid, get the piece, once. Whether raid gear SHOULD roll against its chance is an owner
# decision, not a content one -- see docs/AGENT_BACKLOG.md.
GRADE_GEAR = {
    "Common": [("gear_weir_courser", 1.0)],       # Green
    "Elite":  [("gear_relay_treads", 1.0)],       # Blue
    "Deadly": [("gear_relay_charger", 1.0)],      # Blue mount
    "Mythic": [("gear_sable_courser", 1.0)],      # Purple mount; nothing Orange comes off a raid
}
# The one Orange reagent the Field line ends on, and the only place it comes from.
MYTHIC_TAIL = [("mat_colossus_filament", 0.0008), ("mat_leviathan_baleen", 0.0015)]


def qty(base, diff):
    return max(1, int(round(base * QTY[diff])))


def chance(base, diff):
    return round(min(0.95, base * CHANCE[diff]), 6)


def quest_table(table_id, chapter, zone, is_boss):
    reagent, base_qty = ZONE_REAGENT[(chapter, zone)]
    gear = CHAPTER_GEAR.get(chapter, [])
    deep = DEEP_DROPS.get((chapter, zone), []) if is_boss else []
    difficulties = collections.OrderedDict()
    for diff in ("Normal", "Hard", "Legendary", "Nightmare"):
        # A boss is worth roughly a zone's worth of ordinary nodes, so it pays more of
        # everything rather than paying something different.
        boss_mult = 2.0 if is_boss else 1.0
        entry = collections.OrderedDict()
        entry["guaranteedDrops"] = [{
            "itemId": reagent,
            "quantity": qty(base_qty * boss_mult, diff),
            "chance": 1.0,
        }]
        gear_drops = [{
            "gearDefinitionId": gid,
            "quantity": 1,
            "chance": chance(c * boss_mult, diff),
            "rareScaling": True,
        } for gid, c in gear]
        for kind, ident, c, _why in deep:
            if kind == "gear":
                gear_drops.append({
                    "gearDefinitionId": ident, "quantity": 1,
                    "chance": chance(c, diff), "rareScaling": True,
                })
        if gear_drops:
            entry["gearDrops"] = gear_drops
        item_chance = [{
            "itemId": ident, "quantity": 1,
            "chance": chance(c, diff), "rareScaling": True,
        } for kind, ident, c, _why in deep if kind == "item"]
        if item_chance:
            entry["chanceDrops"] = item_chance
        difficulties[diff] = entry
    return collections.OrderedDict([
        ("id", table_id), ("type", "Quest"), ("difficulties", difficulties)])


def raid_table(table_id, grade, base_hp):
    reagent = GRADE_REAGENT.get(grade, "mat_mire_ichor")
    gear = GRADE_GEAR.get(grade, [])
    # Eight rungs spanning 0.05% to 40% of the Normal health pool. A raid a company fights
    # for two days should pay along the way, not only at the end.
    fractions = [0.0005, 0.002, 0.005, 0.012, 0.03, 0.07, 0.15, 0.40]
    difficulties = collections.OrderedDict()
    for diff in ("Normal", "Hard", "Legendary", "Nightmare"):
        rungs = []
        for i, frac in enumerate(fractions):
            rung = collections.OrderedDict()
            rung["damageThreshold"] = max(100, int(base_hp * frac))
            rung["contributionPercent"] = 0.0
            rung["unassignedStatPoints"] = 1 + i // 2
            rung["attackPoints"] = 0
            rung["defensePoints"] = 0
            rung["discernmentPoints"] = 0
            rung["itemDrops"] = [{
                "itemId": reagent,
                "quantity": qty(1 + i // 3, diff),
                "chance": round(min(0.9, 0.35 + i * 0.06), 4),
            }]
            # The top two rungs are the only place the Field line's Orange reagents exist.
            if grade == "Mythic" and i >= len(fractions) - 2:
                for ident, c in MYTHIC_TAIL:
                    rung["itemDrops"].append({
                        "itemId": ident, "quantity": 1, "chance": chance(c, diff),
                    })
            rung["magicDrops"] = []
            rung["unitDrops"] = []
            rung["legionDrops"] = []
            # TOP RUNG ONLY. Cumulative + unconditional means any earlier rung would multiply.
            rung["gearDrops"] = [{
                "gearDefinitionId": gid, "quantity": 1, "chance": 1.0,
            } for gid, _c in gear] if i == len(fractions) - 1 else []
            rungs.append(rung)
        difficulties[diff] = collections.OrderedDict([
            ("minContributionPercent", 0.0),
            ("onHitDrops", None),
            ("thresholdRewards", rungs),
        ])
    return collections.OrderedDict([
        ("id", table_id), ("type", "Raid"), ("difficulties", difficulties)])


def main():
    qp, rp, lp = CONTENT / "quests.json", CONTENT / "raids.json", CONTENT / "loot_tables.json"
    quests = json.load(io.open(qp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    raids = json.load(io.open(rp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    tables = json.load(io.open(lp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    have = {t["id"] for t in tables}

    wired_q = 0
    for n in quests:
        if n.get("lootTableId"):
            continue                                  # legacy q001-q005 keep their own tables
        ch, zo = n["chapter"], n["zoneIndex"]
        if (ch, zo) not in ZONE_REAGENT:
            continue
        is_boss = n.get("nodeType") == "Boss"
        tid = "lt_zone_c%dz%d%s" % (ch, zo, "b" if is_boss else "")
        if tid not in have:
            tables.append(quest_table(tid, ch, zo, is_boss))
            have.add(tid)
        n["lootTableId"] = tid
        wired_q += 1

    wired_r = 0
    for r in raids:
        if r.get("lootTableId"):
            continue
        tid = "lt_" + r["id"]
        if tid not in have:
            tables.append(raid_table(tid, r.get("grade", "Common"), int(r.get("baseHp", 100000))))
            have.add(tid)
        r["lootTableId"] = tid
        wired_r += 1

    for path, data in ((qp, quests), (rp, raids), (lp, tables)):
        io.open(path, "w", encoding="utf-8", newline="\n").write(
            json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    print("quest nodes wired: %d | raids wired: %d | loot tables now: %d"
          % (wired_q, wired_r, len(tables)))


if __name__ == "__main__":
    main()
