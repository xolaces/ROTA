#!/usr/bin/env python3
"""Eighteen recipes, and the reason all three reagent axes exist.

THE DESIGN THIS COMMITS TO. There are now three independent ways a material can be earned:

    ROAD reagents   quest drops        you spent ENERGY
    FIELD reagents  raid thresholds    you spent STAMINA
    TAG reagents    tagged raids       you fought a particular KIND of thing

A capstone recipe asks for one of each. That is the whole point of splitting them: "grind more",
"grind differently" and "go kill something specific" are three different requests, and a recipe that
can only express one of them is a recipe that can only ever say "grind more".

It is also the structural answer to a single-pool build. No amount of energy buys a Field reagent and
no amount of either buys a Leviathan Tooth — you have to go and fight a Horror.

THE LADDER. Lower recipes ask for one or two axes and are affordable inside a chapter; the capstones
ask for all three plus a real gold cost, which is the only repeatable gold sink in the game that a
player actually wants to use.

VALIDATION THIS RESPECTS (CraftingRecipeProvider throws at boot on each): no recipe consumes its own
output; no ingredient is listed twice; units and legions are own-once so their recipes take and make
exactly one; every id resolves.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"


def item(id_, n=1):
    return {"kind": "Item", "id": id_, "quantity": n}


def gear(id_, n=1):
    return {"kind": "Gear", "id": id_, "quantity": n}


def unit(id_):
    return {"kind": "Unit", "id": id_, "quantity": 1}


def recipe(id_, name, desc, category, out_kind, out_id, ingredients, gold, out_qty=1, event=None):
    return {
        "id": id_, "name": name, "description": desc, "category": category,
        "outputKind": out_kind, "outputId": out_id, "outputQuantity": out_qty,
        "ingredients": ingredients, "goldCost": gold, "eventKey": event,
        "iconPath": "icons/craft/" + id_.replace("craft_", "") + ".png",
    }


NEW = [
    # ══ TIER 1 — one axis. Affordable inside the chapter that teaches it. ═════════════════════
    recipe("craft_warrens_helm", "Warrens Lamp-Hood",
           "A hood, a bracket, and a lamp. The Weir has never improved on it and has stopped trying.",
           "General", "Gear", "gear_warrens_lamp_hood",
           [item("mat_causeway_ash", 6), item("mat_warren_teeth", 4)], 3_000),
    recipe("craft_marchwatch_coat", "Long Marchwatch Coat",
           "Cut to the knee and lined against the wind, because most of the job is weather.",
           "General", "Gear", "gear_marchwatch_coat",
           [item("mat_hollow_reed", 8), item("mat_vanguard_banner", 2)], 4_000),
    recipe("craft_gravesalt_batch", "A Warden's Measure of Gravesalt",
           "The Watch packs it around anything it has to move twice. Six jars is a season.",
           "General", "Gear", "gear_gravewarden_wades",
           [item("mat_gravesalt", 6), item("mat_pitch_seal", 3)], 6_500),

    # ══ TIER 2 — two axes. A pool AND a kind of enemy. ════════════════════════════════════════
    recipe("craft_gravewarden_coat", "Gravewarden's Coat",
           "Heavy canvas with iron at the forearms, because the thing you are moving sometimes moves back.",
           "General", "Gear", "gear_gravewarden_coat",
           [item("mat_keepwall_mortar", 4), item("mat_stag_tendon", 2), item("mat_barrow_iron", 2)],
           14_000),
    recipe("craft_drowned_helm", "Salvager's Helm",
           "Sealed at the collar with a glass plate. The Vaults are dark before they are deep.",
           "General", "Gear", "gear_drowned_helm",
           [item("mat_rime_glass", 3), item("mat_brood_chitin", 3), item("mat_cork_iron", 2)],
           15_000),
    recipe("craft_wrought_visor", "Breaker's Visor",
           "Slit-narrow and backed in lead. A Wrought does not aim, which makes it worse, not better.",
           "General", "Gear", "gear_wrought_visor",
           [item("mat_cinder_salt", 4), item("mat_siege_lead", 3), item("mat_command_sigil", 2)],
           38_000),
    recipe("craft_choir_stole", "Resonant Stole",
           "It hums a half-tone under any sung note. The Choir considers this agreement.",
           "General", "Gear", "gear_choir_stole",
           [item("mat_starhollow_dust", 4), item("mat_resonant_brass", 3), item("mat_amber_rot", 2)],
           40_000),

    # ══ TIER 3 — CAPSTONES. All three axes, plus gold. ════════════════════════════════════════
    # Road + Field + Tag. You cannot buy your way past any one of them with the other two.
    recipe("craft_wroughtbreaker_gauntlets", "Wroughtbreaker Gauntlets",
           "Built to hold a bar against a moving core until the order stops. Most pairs are used once.",
           "Special", "Gear", "gear_wrought_gauntlets",
           [item("mat_cinder_salt", 6),          # Road   — energy
            item("mat_wrathslag", 3),            # Field  — stamina
            item("mat_command_sigil", 4),        # Tag    — Construct
            item("mat_siege_lead", 2)],
           85_000),
    recipe("craft_deepwatch_gauntlets", "Gravewarden's Gauntlets",
           "Iron over leather over iron. Whatever is down there does not get to hold your hand.",
           "Special", "Gear", "gear_gravewarden_gauntlets",
           [item("mat_rime_glass", 5),           # Road
            item("mat_stag_tendon", 3),          # Field
            item("mat_barrow_iron", 3),          # Tag — Undead
            item("mat_pitch_seal", 2)],
           52_000),
    recipe("craft_choir_crown", "Listener's Circlet",
           "Thin, and open at the ears by design. The Choir does not cover what it uses.",
           "Special", "Gear", "gear_choir_crown",
           [item("mat_starhollow_dust", 6),      # Road
            item("mat_leviathan_baleen", 2),     # Field
            item("mat_glutbound_core", 3),       # Tag — Shadow
            item("mat_resonant_brass", 2)],
           95_000),

    # ══ MOUNTS — the expensive tier of every set, and priced like it ══════════════════════════
    recipe("craft_warrens_pitpony", "Warrens Pit-Pony",
           "Small, foul-tempered, and unbothered by the dark. Nobody has ever bred one on purpose twice.",
           "General", "Gear", "gear_warrens_pitpony",
           [item("mat_warren_teeth", 8), item("mat_emberfall_slag", 6), item("mat_mire_ichor", 4)],
           26_000),
    recipe("craft_gravewarden_dray", "Gravewarden's Dray",
           "Bred to stand still while unpleasant work happens behind it. The rarest quality a horse can have.",
           "General", "Gear", "gear_gravewarden_dray",
           [item("mat_barrow_iron", 5), item("mat_keepwall_mortar", 6), item("mat_brood_chitin", 4)],
           64_000),

    # ══ UNITS — the Dawn pattern: a named thing in, a better-named thing out ══════════════════
    recipe("craft_gorruk_oathbound", "Gorruk, Oathbound",
           "The Oroc agreed to hold a position. Oathsteel is how the Weir makes an agreement heavier.",
           "General", "Unit", "gen_makh",
           [unit("gen_gorruk"), item("mat_oathsteel", 3), item("mat_barrow_iron", 4)], 45_000),
    recipe("craft_durn_sigilwise", "Durn, Sigil-Wise",
           "He was wrong twice and kept both notes. This is what the notes were for.",
           "General", "Unit", "gen_brannoc",
           [unit("gen_durn"), item("mat_command_sigil", 5), item("mat_colossus_filament", 1)], 70_000),

    # ══ THE DEEP END — one recipe per chase set, and they are meant to hurt ═══════════════════
    recipe("craft_sovereign_signet", "Sovereign's Signet",
           "Opens the Gauntlet's inner registry. What is written there is the pact's other half.",
           "Special", "Gear", "gear_sovereign_signet",
           [item("mat_sovereign_scale", 2),      # Tag — Dragon, and the only source
            item("mat_starhollow_dust", 10),     # Road
            item("mat_colossus_filament", 2),    # Field, Orange
            item("mat_kronarch_seal", 3)],       # Tag — Legion
           220_000),
    recipe("craft_censer", "The Perpetual Censer",
           "Lit some time during the Age of Dawn and never once since. Whatever is in it, there is "
           "still some left. The Choir has a theory. He has never confirmed or denied it and appears "
           "to find the question restful.",
           "Special", "Gear", "gear_stoned_censer",
           [item("mat_censer_resin", 3), item("mat_brimstone_slag", 6),
            item("mat_glutbound_core", 4), item("mat_quiet_lamp_oil", 1)],
           180_000),
    recipe("craft_stoned_slippers", "Slippers of the Unwalked Path",
           "Immaculate. Not a scuff on them. Making a pair requires leather that has never been "
           "walked on, which is harder to source than it sounds and much harder to explain.",
           "Special", "Gear", "gear_stoned_slippers",
           [item("mat_unwalked_leather", 4), item("mat_sable_thread", 5),
            item("mat_deepwood_heart", 2), item("mat_amber_rot", 6)],
           160_000),
    recipe("craft_hell_goat", "Extremely Relaxed Hell-Goat",
           "It will carry you anywhere at exactly one speed. Attempts to hurry it have never once "
           "succeeded and, by every account, have never once been forgiven.",
           "Special", "Gear", "gear_stoned_goat",
           [item("mat_censer_resin", 2), item("mat_brimstone_slag", 10),
            item("mat_deepwood_heart", 3), item("mat_leviathan_tooth", 2)],
           260_000),
]


def main():
    p = CONTENT / "recipes.json"
    data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    have = {r["id"] for r in data}
    added = [r for r in NEW if r["id"] not in have]
    data.extend(added)
    io.open(p, "w", encoding="utf-8", newline="\n").write(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    cats = collections.Counter(r["category"] for r in data)
    kinds = collections.Counter(r["outputKind"] for r in data)
    gold = sorted(r["goldCost"] for r in added)
    print("recipes: +%d (total %d)" % (len(added), len(data)))
    print("  by category: %s   by output: %s" % (dict(sorted(cats.items())), dict(sorted(kinds.items()))))
    print("  gold cost range of the new ones: %s -> %s" % (f"{gold[0]:,}", f"{gold[-1]:,}"))


if __name__ == "__main__":
    main()
