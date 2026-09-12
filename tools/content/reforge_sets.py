#!/usr/bin/env python3
"""Proper raid loot: raids drop the parts, crafting makes the better piece.

THE OWNER'S SPEC (2026-09-11). Quests drop gear at a grind rate, every piece in a pool at the
same odds — "1/50 or more per click". Raids do not drop gear; they drop COMPONENTS for an upgraded
version of it, on the damage ladder, so the share of the fight you carried is the share of the
odds you get. Crafting turns the base piece plus the components into the reforged piece. The
mount's component is the rarest thing a raid gives up: at the top of the ladder on Nightmare it
is a 10% chance of one.

WHAT THIS WRITES, all of it re-runnable — every run strips what the last one wrote and writes it
again, so a number changed below is a number changed in the game:

  gear.json         one reforged twin per set piece: same slot, same rarity, same art, bonuses x
                    REFORGE_MULT, in a set of its own (<set>_reforged). Twelve sets; Conscript is left out because nothing on
                    the live server drops or sells it (a gap this file does not pretend to fix).
  items.json        two materials per set: a Scrap (body pieces) and a Tack (the mount).
  recipes.json      one Reforge recipe per piece: the base piece + scraps (or tack) + gold.
  loot_tables.json  scrap and tack lines on every rung of every raid that carries the set, at
                    per-rung odds that compound to SCRAP_TOP / TACK_TOP for a player who clears
                    the whole ladder; three new tables for the guild raids, which dropped nothing;
                    and every quest zone's gear pool re-rated to QUEST_GEAR_RATE, equal per piece.
  guild_raids.json  lootTableId wired to the new tables.

WHERE A SET'S PARTS COME FROM is where its pieces already drop. A set whose pieces fall in the
chapter-3 zones gets its scraps from the chapter-3 raids, so one chapter is one set's whole loop:
quest the ground for the piece, raid the boss for the parts. The three guild raids and the two
World raids are the exceptions, assigned by fiction — the Leviathan for the Drowned, Kronarch for
Sable Vein, the Warlord for the Stoned Devil, and the World raids for Pano's vanguard kit, which
is a quest chase set and otherwise has no raid.

THE LADDER MATHS. A rung is banked cumulatively (RaidService collects every rung a player passed),
so a line with per-rung chance q on every rung of an N-rung ladder gives a full-ladder player
1 - (1-q)^N. q is solved from the top-of-ladder target, which is the number the owner actually
talks about, and a player at rung k gets 1 - (1-q)^k — odds that climb with the damage.
"""
import io
import json
import math
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

# ── Sets, and what their parts are called. (display name, scrap blurb, tack blurb)
SETS = {
    "set_weir":           ("Weir",          "Rivets and strap-iron off Iron Weir issue. Enough of it reforges a piece.",
                                            "Bit, buckle and shoe-iron from a Weir courser's tack. They do not come off easily."),
    "set_marchwatch":     ("Marchwatch",    "Oiled wool and storm-cloth cut from Marchwatch kit, still smelling of rain.",
                                            "Harness leather from a Marchwatch rounder, waxed against a weather that never let up."),
    "set_warrens":        ("Warrens",       "Soot-black leather and brass fittings out of the pits. Every piece was worn down there.",
                                            "A pit pony's tack — squat brass rings and a lamp-hook, black to the core."),
    "set_relay":          ("Relay",         "Cream canvas, blue piping, brass. Last Watch relay kit, taken off a relay runner.",
                                            "A relay charger's tack, signal-lamp bracket still on the saddle."),
    "set_gravewarden":    ("Gravewarden",   "Barrow-iron plate and pitch-sealed canvas, with a line of gravesalt in every fold.",
                                            "Dray harness, black pitch on iron. It was made to pull weight out of the ground."),
    "set_drowned":        ("Drowned",       "Verdigris copper and sealed cork. It came up out of the water and is still wet.",
                                            "Tack from a drowned courser: glass-beaded, copper-buckled, and it does not rust."),
    "set_sable_vein":     ("Sable Vein",    "Matte black cloth with a single gold lozenge. House work; the seam is invisible.",
                                            "A Sable courser's tack, narrow and severe, gold at one point only."),
    "set_wroughtbreaker": ("Wroughtbreaker", "Lead-grey slab iron with a cracked orange core. Siege metal, not armour.",
                                            "A destrier's barding off a siege line — heavy enough to need two hands."),
    "set_choir":          ("Choir",         "Resonant brass and pale bone. Strike it and it holds a note for too long.",
                                            "Palfrey tack in brass and bone, cut open at the ears so the animal can hear the choir."),
    "set_sovereign":      ("Sovereign",     "Crimson scale on antique gold, edge-plated. Regalia; it wants a throne under it.",
                                            "Wyrm tack. There is no word for what it is made of that anyone will say aloud."),
    "set_stoned_devil":   ("Stoned Devil",  "Maroon and cream with aged brass, soft to the touch. Nothing about it is sharp.",
                                            "A goat's tack. Someone hung a small brass censer off it and it is still smoking."),
    "set_pano":           ("Vanguard",      "White enamel, deep blue and gold, four-pointed star. Pano's line wore this and did not come back.",
                                            "Barding from Pano's own steed, star still bright on the chamfron."),
}

# ── Which raids drop which set's parts. Campaign raids follow the chapter the set's pieces drop
#    in (see the docstring); guild and World raids are assigned by fiction.
RAID_SETS = {
    "raid_c1z1b": ["set_weir", "set_marchwatch"],
    "raid_c1z2b": ["set_weir", "set_marchwatch"],
    "raid_c2z1b": ["set_weir", "set_marchwatch", "set_warrens"],
    "raid_c2z2b": ["set_weir", "set_marchwatch", "set_warrens"],
    "raid_c2z3b": ["set_weir", "set_marchwatch", "set_warrens"],
    "raid_c3z0b": ["set_relay", "set_gravewarden", "set_drowned"],
    "raid_c3z1b": ["set_relay", "set_gravewarden", "set_drowned"],
    "raid_c3z2b": ["set_relay", "set_gravewarden", "set_drowned"],
    "raid_c3z3b": ["set_relay", "set_gravewarden", "set_drowned"],
    "raid_c4z0b": ["set_relay", "set_gravewarden"],
    "raid_c4z1b": ["set_relay", "set_gravewarden"],
    "raid_c4z2b": ["set_relay", "set_gravewarden"],
    "raid_c4z3b": ["set_relay", "set_gravewarden"],
    "raid_c5z0b": ["set_sable_vein", "set_wroughtbreaker", "set_stoned_devil"],
    "raid_c5z1b": ["set_sable_vein", "set_wroughtbreaker", "set_stoned_devil"],
    "raid_c5z2b": ["set_sable_vein", "set_wroughtbreaker", "set_stoned_devil"],
    "raid_c5z3b": ["set_sable_vein", "set_wroughtbreaker", "set_stoned_devil"],
    "raid_c5z4b": ["set_sable_vein", "set_wroughtbreaker", "set_stoned_devil"],
    "raid_c6z0b": ["set_sable_vein", "set_choir", "set_sovereign", "set_stoned_devil"],
    "raid_c6z1b": ["set_sable_vein", "set_choir", "set_sovereign", "set_stoned_devil"],
    "raid_c6z2b": ["set_sable_vein", "set_choir", "set_sovereign", "set_stoned_devil"],
    "raid_c6z3b": ["set_sable_vein", "set_choir", "set_sovereign", "set_stoned_devil"],
    "raid_c6z4b": ["set_sable_vein", "set_choir", "set_sovereign", "set_stoned_devil"],
    "raid_lastwatch_relay": ["set_sable_vein", "set_sovereign"],
    "raid_lastwatch_vault": ["set_sable_vein", "set_sovereign"],
    "raid_lastwatch_lamp":  ["set_sable_vein", "set_sovereign"],
    "guild_raid_warlord":   ["set_stoned_devil"],
    "guild_raid_leviathan": ["set_drowned"],
    "guild_raid_titan":     ["set_sable_vein"],
    "raid_ironcolossus":    ["set_wroughtbreaker", "set_pano"],
    "raid_malachar":        ["set_sovereign", "set_pano"],
}

# ── Odds at the TOP of the ladder — a player who cleared every rung — of at least one drop.
#    Spread over every rung, so the odds climb with the share of the fight carried.
SCRAP_TOP = {"Normal": 0.12, "Hard": 0.18, "Legendary": 0.24, "Nightmare": 0.30}
TACK_TOP  = {"Normal": 0.04, "Hard": 0.06, "Legendary": 0.08, "Nightmare": 0.10}

# ── The reforged piece and its price.
REFORGE_MULT = 1.5                                            # bonuses, rounded half up
SCRAPS_PER_PIECE = {"Green": 3, "Blue": 4, "Purple": 5, "Orange": 6}
TACK_PER_MOUNT = 3
GOLD = {"Green": 2_000, "Blue": 8_000, "Purple": 25_000, "Orange": 75_000}
MOUNT_GOLD_MULT = 2

# ── Quest gear: chance per click at Normal, by chapter, for EVERY set piece in the zone's pool.
#    Difficulty and the boss node scale it exactly as the shipped tables already did.
QUEST_GEAR_RATE = {1: 1 / 50, 2: 1 / 50, 3: 1 / 60, 4: 1 / 60, 5: 1 / 80, 6: 1 / 80, 7: 1 / 100}
QUEST_DIFF  = {"Normal": 1.0, "Hard": 1.15, "Legendary": 1.3, "Nightmare": 1.5}
BOSS_NODE_MULT = 2.0
# Two Marchwatch pieces never dropped anywhere. They join the chapter-2 pools, where the rest of
# the set falls.
ORPHANED_PIECES = {2: ["gear_marchwatch_tally", "gear_marchwatch_boots"]}

# ── Guild raid ladders, in the campaign tables' shape (retune_raid_health.py's fractions).
GUILD_FRACTIONS = [0.0005, 0.002, 0.005, 0.012, 0.03, 0.07, 0.15, 0.40]
GUILD_STAT_POINTS = [1, 1, 2, 2, 3, 3, 4, 4]
GUILD_REAGENT_CHANCE = [0.35, 0.41, 0.47, 0.53, 0.59, 0.65, 0.71, 0.77]
GUILD_REAGENT_QTY = [1, 1, 1, 2, 2, 2, 3, 3]
GUILD_QTY  = {"Normal": 1.0, "Hard": 1.5, "Legendary": 2.0, "Nightmare": 3.0}
GUILD_CHANCE = {"Normal": 1.0, "Hard": 1.15, "Legendary": 1.3, "Nightmare": 1.5}
# (field reagent, the boss's own relic dropped on the fifth rung) — Wrath-slag is the Mythic
# field reagent and all three are Mythic-sized fights.
GUILD_REAGENTS = {
    "guild_raid_warlord":   ("mat_wrathslag", "mat_command_sigil"),
    "guild_raid_leviathan": ("mat_wrathslag", "mat_leviathan_tooth"),
    "guild_raid_titan":     ("mat_wrathslag", "mat_kronarch_seal"),
}

DIFFICULTIES = ["Normal", "Hard", "Legendary", "Nightmare"]


def load(name):
    return json.load(io.open(CONTENT / name, encoding="utf-8"))


def save(name, data):
    io.open(CONTENT / name, "w", encoding="utf-8", newline="\n").write(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n")


def scrap_id(set_id):
    return "mat_" + set_id[4:] + "_scrap"


def tack_id(set_id):
    return "mat_" + set_id[4:] + "_tack"


def reforged_id(gear_id):
    return gear_id + "_reforged"


def per_rung(top, rungs):
    """The per-rung chance that compounds to `top` across `rungs` cumulative rungs."""
    return round(1.0 - (1.0 - top) ** (1.0 / rungs), 4)


def tidy(v):
    """Three significant figures, as retune_raid_health.py writes thresholds."""
    if v < 1000:
        return int(round(v))
    mag = 10 ** (int(math.log10(v)) - 2)
    return int(round(v / mag) * mag)


# ── gear.json ────────────────────────────────────────────────────────────────────────────────
def reforge_gear(gear):
    base = [g for g in gear if not g["id"].endswith("_reforged")]
    out = []
    for g in base:
        if g.get("setId") not in SETS:
            continue
        r = dict(g)
        r["id"] = reforged_id(g["id"])
        r["name"] = g["name"] + " (Reforged)"
        r["description"] = ("Reforged with " + SETS[g["setId"]][0] +
                            (" Tack. " if g["slot"] == "Mount" else " Scrap. ") + g["description"])
        r["bonusAttack"]  = int(math.floor(g["bonusAttack"]  * REFORGE_MULT + 0.5))
        r["bonusDefense"] = int(math.floor(g["bonusDefense"] * REFORGE_MULT + 0.5))
        # Same art: the engine draws the rarity frame, the name carries the difference, and the
        # reforged piece is not a new object to draw — it is the same object, made properly.
        r["iconPath"] = g["iconPath"]
        # The base piece's next-tier pointer is the base piece's; the reforged one is the end of
        # its own line.
        r["upgradesTo"] = None
        # Its own set: eight reforged pieces, one per slot, one rarity. GearDefinitionProvider
        # refuses a set that claims a slot twice, and when set bonuses land a reforged set should
        # be able to carry a better one than the set it came from.
        r["setId"] = g["setId"] + "_reforged"
        # The link back, for the client: draw me with my base piece's art, mark me reforged.
        r["reforgedFrom"] = g["id"]
        out.append(r)
    return base + out


# ── items.json ───────────────────────────────────────────────────────────────────────────────
def reforge_items(items, gear):
    ours = {scrap_id(s) for s in SETS} | {tack_id(s) for s in SETS}
    kept = [i for i in items if i["id"] not in ours]
    rarity_of = {}
    for g in gear:
        if g.get("setId") in SETS:
            rarity_of[g["setId"]] = g["rarity"]
    new = []
    for set_id, (name, scrap_blurb, tack_blurb) in SETS.items():
        for id_, nm, blurb in ((scrap_id(set_id), name + " Scrap", scrap_blurb),
                               (tack_id(set_id),  name + " Tack",  tack_blurb)):
            new.append({
                "id": id_, "name": nm, "description": blurb, "rarity": rarity_of[set_id],
                "type": "Material", "artKey": id_, "statPointsOnUse": 0,
                "isCraftingIngredient": True, "summonRaidId": None, "summonDifficulty": None,
                "summonSize": None, "upgradesTo": None, "tags": ["Reforge"],
            })
    return kept + new


# ── recipes.json ─────────────────────────────────────────────────────────────────────────────
def reforge_recipes(recipes, gear):
    kept = [r for r in recipes if not r["id"].endswith("_reforged")]
    new = []
    for g in gear:
        if g.get("setId") not in SETS or g["id"].endswith("_reforged"):
            continue
        mount = g["slot"] == "Mount"
        part = tack_id(g["setId"]) if mount else scrap_id(g["setId"])
        count = TACK_PER_MOUNT if mount else SCRAPS_PER_PIECE[g["rarity"]]
        gold = GOLD[g["rarity"]] * (MOUNT_GOLD_MULT if mount else 1)
        part_name = SETS[g["setId"]][0] + (" Tack" if mount else " Scrap")
        new.append({
            "id": "craft_" + g["id"][5:] + "_reforged",
            "name": g["name"] + " (Reforged)",
            "description": "Reforge the %s around %d %s. The piece is consumed; what comes back "
                           "is the same piece with nothing left out." % (g["name"], count, part_name),
            "category": "Reforge",
            "outputKind": "Gear",
            "outputId": reforged_id(g["id"]),
            "outputQuantity": 1,
            "ingredients": [
                {"kind": "Gear", "id": g["id"], "quantity": 1},
                {"kind": "Item", "id": part, "quantity": count},
            ],
            "goldCost": gold,
            "eventKey": None,
            "iconPath": g["iconPath"],
        })
    return kept + new


# ── loot_tables.json ─────────────────────────────────────────────────────────────────────────
def strip_parts(itemdrops):
    ours = {scrap_id(s) for s in SETS} | {tack_id(s) for s in SETS}
    return [d for d in (itemdrops or []) if d["itemId"] not in ours]


def add_part_lines(table, sets):
    """Scrap and tack lines on every rung, per difficulty, compounding to the top targets."""
    for diff, block in table["difficulties"].items():
        rungs = block.get("thresholdRewards") or []
        n = len(rungs)
        if n == 0:
            continue
        for rung in rungs:
            rung["itemDrops"] = strip_parts(rung.get("itemDrops"))
        for set_id in sets:
            qs = per_rung(SCRAP_TOP[diff], n)
            qt = per_rung(TACK_TOP[diff], n)
            for rung in rungs:
                rung["itemDrops"].append({"itemId": scrap_id(set_id), "quantity": 1, "chance": qs})
                rung["itemDrops"].append({"itemId": tack_id(set_id), "quantity": 1, "chance": qt})


def guild_table(raid):
    field, relic = GUILD_REAGENTS[raid["id"]]
    difficulties = {}
    for diff in DIFFICULTIES:
        rungs = []
        for i, frac in enumerate(GUILD_FRACTIONS):
            drops = [{"itemId": field,
                      "quantity": max(1, int(round(GUILD_REAGENT_QTY[i] * GUILD_QTY[diff]))),
                      "chance": round(GUILD_REAGENT_CHANCE[i], 2)}]
            if i == 4:
                drops.append({"itemId": relic, "quantity": max(1, int(round(GUILD_QTY[diff]))),
                              "chance": round(min(0.95, 0.22 * GUILD_CHANCE[diff]), 2)})
            rungs.append({
                "damageThreshold": max(100, tidy(raid["baseHp"] * frac)),
                "contributionPercent": 0.0,
                "unassignedStatPoints": GUILD_STAT_POINTS[i],
                "attackPoints": 0, "defensePoints": 0, "discernmentPoints": 0,
                "itemDrops": drops,
            })
        difficulties[diff] = {"minContributionPercent": 0.0, "onHitDrops": None,
                              "thresholdRewards": rungs}
    return {"id": "lt_" + raid["id"], "type": "Raid", "difficulties": difficulties}


def rerate_quest_gear(table, gear_by_id):
    """Every set piece in a zone pool at the chapter's rate; the relic tail is left alone."""
    import re
    m = re.match(r"lt_zone_c(\d+)z\d+(b?)$", table["id"])
    if not m:
        return
    chapter, boss = int(m.group(1)), bool(m.group(2))
    base = QUEST_GEAR_RATE[chapter] * (BOSS_NODE_MULT if boss else 1.0)
    normal = {d["gearDefinitionId"]: d["chance"] for d in table["difficulties"]["Normal"].get("gearDrops") or []}
    for diff, block in table["difficulties"].items():
        drops = block.get("gearDrops") or []
        present = {d["gearDefinitionId"] for d in drops}
        for gid in ORPHANED_PIECES.get(chapter, []):
            if gid not in present:
                drops.append({"gearDefinitionId": gid, "quantity": 1, "chance": 0.0, "rareScaling": True})
        for d in drops:
            g = gear_by_id[d["gearDefinitionId"]]
            # A no-set piece under 0.1% at Normal is one of the deep relics: one home each, odds
            # chosen by hand in wire_drop_tables.py. Not this file's to touch.
            tail = g.get("setId") is None and normal.get(d["gearDefinitionId"], 0.0) < 0.001
            if tail:
                continue
            d["chance"] = round(base * QUEST_DIFF[diff], 5)
        block["gearDrops"] = drops


def main():
    gear = reforge_gear(load("gear.json"))
    save("gear.json", gear)
    gear_by_id = {g["id"]: g for g in gear}

    items = reforge_items(load("items.json"), gear)
    save("items.json", items)

    recipes = reforge_recipes(load("recipes.json"), gear)
    save("recipes.json", recipes)

    tables = [t for t in load("loot_tables.json") if not t["id"].startswith("lt_guild_raid_")]
    by_id = {t["id"]: t for t in tables}
    raids = {r["id"]: r for f in ("raids.json", "guild_raids.json") for r in load(f)}

    guild_raids = load("guild_raids.json")
    for raid in guild_raids:
        t = guild_table(raid)
        tables.append(t)
        by_id[t["id"]] = t
        raid["lootTableId"] = t["id"]
    save("guild_raids.json", guild_raids)

    for raid_id, sets in RAID_SETS.items():
        raid = raids[raid_id]
        table_id = raid["lootTableId"] if raid["lootTableId"] else "lt_" + raid_id
        add_part_lines(by_id[table_id], sets)

    for t in tables:
        if t["type"] == "Quest":
            rerate_quest_gear(t, gear_by_id)
    save("loot_tables.json", tables)

    n_reforged = sum(1 for g in gear if g["id"].endswith("_reforged"))
    print("reforged gear: %d   materials: %d   recipes: %d   raids carrying parts: %d   guild tables: %d"
          % (n_reforged, 2 * len(SETS), sum(1 for r in recipes if r["category"] == "Reforge"),
             len(RAID_SETS), len(guild_raids)))
    for diff in DIFFICULTIES:
        print("  %-9s 8-rung ladder: scrap %.4f/rung -> %.0f%% at the top; tack %.4f/rung -> %.0f%%"
              % (diff, per_rung(SCRAP_TOP[diff], 8), SCRAP_TOP[diff] * 100,
                 per_rung(TACK_TOP[diff], 8), TACK_TOP[diff] * 100))


if __name__ == "__main__":
    main()
