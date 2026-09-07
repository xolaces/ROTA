#!/usr/bin/env python3
"""Eight new gear sets, and SetId backfilled onto the five that already shipped.

WHY SETS AT ALL. Set bonuses are still PHASE-2, so nothing reads SetId in combat yet. Authoring the
sets NOW anyway is the difference between tuning a set later and re-authoring one: the grouping, the
slot coverage and the tier are the expensive parts, and the bonus is a number the owner adds on top.

BALANCE OF THE ADDITION. Two sets per tier, so no tier becomes the obvious one:

    Green   Warrens Kit          Marchwatch
    Blue    Gravewarden          Drowned Coast
    Purple  Wroughtbreaker       Choir Vestments
    Orange  Sovereign's Regalia  The Stoned Devil

Each set is eight pieces, one per slot, so every set is completable and no set competes with itself.
The power curve is the one the shipped gear already implies — Grey ~1 total, Green 4, Blue 10,
Purple 18, Orange 30 — with mounts carrying roughly three times a body slot, as gear_pano_steed does.

THE SETS ANSWER THE NEW RAID TAGS, in fiction if not yet in mechanics. The Warrens Kit is what the
Iron Weir issues for goblin work; Gravewarden is Last Watch grave-work; Wroughtbreaker is siege gear
for the Wrought. When set bonuses land, the affinity they should grant is already written on the tin.

THE STONED DEVIL is the owner's requested farewell to Dawn's Drunken Angel — the same joke told from
the other end. It is deliberately the ONLY set that winks, it is dry rather than loud, and the house
rule survives it: the pieces are funny because the devil is sincere, not because the game is.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

SLOTS = ["Head", "Neck", "Torso", "Ring1", "Ring2", "Mount", "Boots", "Gloves"]

# rarity -> (atk, def) per body slot; the mount takes MOUNT_MULT of the total.
CURVE = {"Green": (2, 2), "Blue": (5, 5), "Purple": (9, 9), "Orange": (15, 15)}
MOUNT_MULT = 3


def piece(set_id, slot, id_, name, desc, rarity, atk, dfn, proc=None, procpct=None):
    d = {
        "id": id_, "name": name, "description": desc, "rarity": rarity, "slot": slot,
        "bonusAttack": atk, "bonusDefense": dfn,
        "procChance": proc, "procPercent": procpct,
        "iconPath": "icons/gear/" + id_.replace("gear_", "") + ".png",
        "setId": set_id,
    }
    return d


def build(set_id, rarity, lean, rows, mount_proc=None, mount_procpct=None):
    """rows: slot -> (id, name, description). `lean` biases atk/def without changing the total."""
    base_a, base_d = CURVE[rarity]
    total = base_a + base_d
    out = []
    for slot, (id_, name, desc) in rows.items():
        if slot == "Mount":
            a = d = (total * MOUNT_MULT) // 2
            out.append(piece(set_id, slot, id_, name, desc, rarity, a, d, mount_proc, mount_procpct))
            continue
        bias = lean.get(slot, 0)
        a = max(0, base_a + bias)
        d = max(0, total - a)
        out.append(piece(set_id, slot, id_, name, desc, rarity, a, d))
    return out


NEW = []

# ══ GREEN · WARRENS KIT ═══════════════════════════════════════════════════════════════════════
# Iron Weir issue for goblin work. Cheap, replaceable, and shaped by the fact that the warrens are
# low, dark and full of things that bite ankles.
NEW += build("set_warrens", "Green",
             {"Ring1": 2, "Ring2": 2, "Gloves": 1, "Head": -1, "Torso": -1}, {
    "Head":   ("gear_warrens_lamp_hood", "Warrens Lamp-Hood",
               "A hood with a lamp bracket sewn at the temple. Both hands stay free, which in a warren is the whole argument."),
    "Neck":   ("gear_warrens_choker", "Ratter's Choker",
               "Leather, doubled. Goblins go for the throat because it works, so the Weir stopped leaving it bare."),
    "Torso":  ("gear_warrens_jack", "Warrens Jack",
               "Quilted and short-cut. Long coats catch on everything down there, and everything down there catches back."),
    "Ring1":  ("gear_warrens_seal", "Tunnel-Warden's Seal",
               "Marks the bearer as the one who counts everyone back out. It is not a promotion."),
    "Ring2":  ("gear_warrens_knuckle", "Knuckle-Ring of the Weir",
               "Worn on the outside of the glove. Frontier smiths call this an ornament and frontier wardens do not."),
    "Mount":  ("gear_warrens_pitpony", "Warrens Pit-Pony",
               "Small, foul-tempered, and unbothered by the dark. It has walked out of places its riders did not."),
    "Boots":  ("gear_warrens_treads", "Warrens Treads",
               "Nailed flat for wet stone. A warren floor is never dry and never once been level."),
    "Gloves": ("gear_warrens_grips", "Ratter's Grips",
               "Reinforced across the back of the hand. You will be hitting things that are already biting you."),
})

# ══ GREEN · MARCHWATCH ════════════════════════════════════════════════════════════════════════
# The other Green option: the standing watch rather than the raiding party. Where the Warrens Kit
# leans into the strike, this leans into the standing-there.
NEW += build("set_marchwatch", "Green",
             {"Head": 1, "Torso": -2, "Neck": -1, "Boots": -1, "Ring1": 1}, {
    "Head":   ("gear_marchwatch_helm", "Marchwatch Helm",
               "Open-faced, because a watch that cannot see is a wall with a man behind it."),
    "Neck":   ("gear_marchwatch_gorget", "Watchman's Gorget",
               "Plain steel, no device. A marchwatch is not a house and does not want to be mistaken for one."),
    "Torso":  ("gear_marchwatch_coat", "Long Marchwatch Coat",
               "Cut to the knee and lined against the wind. Most of the job is weather."),
    "Ring1":  ("gear_marchwatch_ring", "Ring of the Standing Watch",
               "Given at the end of a first full winter on the line. Most are given posthumously; this one was not."),
    "Ring2":  ("gear_marchwatch_tally", "Tally-Ring",
               "Notched once a season. A warden with a smooth ring is new; one with a worn ring is rare."),
    "Mount":  ("gear_marchwatch_rounder", "Marchwatch Rounder",
               "Trained to walk a circuit and stop at every marker without being asked. It knows the route better than the rider."),
    "Boots":  ("gear_marchwatch_boots", "Marchwatch Boots",
               "Heavy, and worn through at the heel rather than the toe — the wear pattern of a man who stands."),
    "Gloves": ("gear_marchwatch_mitts", "Watch Mitts",
               "Split at the fingertips so a bowstring can still be felt. Frostbite is a slower enemy but it is patient."),
})

# ══ BLUE · GRAVEWARDEN ════════════════════════════════════════════════════════════════════════
# Last Watch grave-work. Answers Undead in the fiction, and reads as a job nobody volunteers for.
NEW += build("set_gravewarden", "Blue",
             {"Head": -1, "Torso": -2, "Ring1": 3, "Ring2": 2, "Gloves": 2, "Neck": 1}, {
    "Head":   ("gear_gravewarden_hood", "Gravewarden's Hood",
               "Waxed against the smell. The Watch is direct about what the job involves."),
    "Neck":   ("gear_gravewarden_seal", "Seal of the Quiet Ground",
               "Worn so a warden can be identified if they do not come back up. It has been needed."),
    "Torso":  ("gear_gravewarden_coat", "Gravewarden's Coat",
               "Heavy canvas with iron at the forearms, because the thing you are moving sometimes moves back."),
    "Ring1":  ("gear_gravewarden_band", "Binding Band",
               "Old sigil-work, worn smooth. It does not hold anything closed any more. The Watch wears it anyway."),
    "Ring2":  ("gear_gravewarden_signet", "Warden's Signet",
               "Authorises the bearer to open a marked grave. There is no ring that authorises closing one."),
    "Mount":  ("gear_gravewarden_dray", "Gravewarden's Dray",
               "Bred to stand still while unpleasant work happens behind it. The rarest quality a horse can have."),
    "Boots":  ("gear_gravewarden_wades", "Marsh Wades",
               "Thigh-high and pitch-sealed. The Hollow Marches are where the Watch does most of this."),
    "Gloves": ("gear_gravewarden_gauntlets", "Gravewarden's Gauntlets",
               "Iron over leather over iron. Whatever is down there does not get to hold your hand."),
})

# ══ BLUE · DROWNED COAST ══════════════════════════════════════════════════════════════════════
# Salvage kit from the Sunken Vaults. Horror-adjacent: the Coast is where the Sundering put things.
NEW += build("set_drowned", "Blue",
             {"Head": 2, "Neck": 2, "Torso": -2, "Ring1": 1, "Boots": -2, "Gloves": 1}, {
    "Head":   ("gear_drowned_helm", "Salvager's Helm",
               "Sealed at the collar with a glass plate. The Vaults are dark before they are deep."),
    "Neck":   ("gear_drowned_torc", "Tidewatch Torc",
               "Cold in cold water, warm in water that is not. Salvagers do not agree on what the warm kind means."),
    "Torso":  ("gear_drowned_harness", "Salvage Harness",
               "Rings and line, no plate. Down there weight is not protection, it is a decision."),
    "Ring1":  ("gear_drowned_seal", "Vault-Seal Ring",
               "Old Guard work. It opens one door in the Sunken Vaults and nobody has found which."),
    "Ring2":  ("gear_drowned_band", "Barnacle Band",
               "Recovered encrusted and left that way. Coast salvagers say a clean ring means a short career."),
    "Mount":  ("gear_drowned_courser", "Coastwise Courser",
               "Sure-footed on wet shale and entirely unwilling to enter water above the knee. It has its reasons."),
    "Boots":  ("gear_drowned_boots", "Shalewalkers",
               "Soled in cork and iron. The Drowned Coast is loose all the way down."),
    "Gloves": ("gear_drowned_grips", "Salvager's Grips",
               "Webbed, tarred, and cut short at the thumb so a knot can still be tied blind."),
})

# ══ PURPLE · WROUGHTBREAKER ═══════════════════════════════════════════════════════════════════
# Siege gear for Order IV. Made by the Iron Weir, who are the only people who still practise it.
NEW += build("set_wroughtbreaker", "Purple",
             {"Head": -2, "Neck": -2, "Torso": -3, "Ring1": 4, "Ring2": 3, "Gloves": 4}, {
    "Head":   ("gear_wrought_visor", "Breaker's Visor",
               "Slit-narrow and backed in lead. A Wrought does not aim, which makes it worse, not better."),
    "Neck":   ("gear_wrought_collar", "Siege Collar",
               "Braced to the shoulders. It exists so the head stays on when the arm stops something heavy."),
    "Torso":  ("gear_wrought_plate", "Wroughtbreaker Plate",
               "Layered against impact rather than edge. Nothing made by the Old Guard bothers with edges."),
    "Ring1":  ("gear_wrought_keyring", "Sigil-Key Ring",
               "Reads a commanding sigil well enough to guess at its order. Guessing is the whole trade."),
    "Ring2":  ("gear_wrought_band", "Breaker's Band",
               "Cast from the melt of a construct that finally stopped. The Weir keeps the melt and the tally."),
    "Mount":  ("gear_wrought_destrier", "Siege Destrier",
               "Trained to stand under a falling thing. Horses are not built for this and it is taught anyway."),
    "Boots":  ("gear_wrought_sabatons", "Breaker's Sabatons",
               "Weighted so a shove does not become a fall. A Wrought's first move is almost always a shove."),
    "Gloves": ("gear_wrought_gauntlets", "Wroughtbreaker Gauntlets",
               "Built to hold a bar against a moving core until the order stops. Most pairs are used once."),
})

# ══ PURPLE · CHOIR VESTMENTS ══════════════════════════════════════════════════════════════════
# The Dawnward Choir: the Ancients are listening, and the Choir has decided that is an invitation.
NEW += build("set_choir", "Purple",
             {"Head": 3, "Neck": 3, "Torso": -3, "Ring1": 2, "Ring2": 2, "Boots": -4, "Gloves": -3}, {
    "Head":   ("gear_choir_crown", "Listener's Circlet",
               "Thin, and open at the ears by design. The Choir does not cover what it uses."),
    "Neck":   ("gear_choir_stole", "Resonant Stole",
               "It hums a half-tone under any sung note. The Choir considers this agreement."),
    "Torso":  ("gear_choir_vestment", "Dawnward Vestment",
               "Undyed, unadorned, and cut so it hangs still in wind. Movement is noise and noise is interference."),
    "Ring1":  ("gear_choir_ring", "Ring of the First Answer",
               "Worn by whoever spoke when something answered. The Choir has four. It will not say to what."),
    "Ring2":  ("gear_choir_band", "Attendant's Band",
               "Given to those who stand and do not sing. Somebody has to be listening to the listeners."),
    "Mount":  ("gear_choir_palfrey", "Choir Palfrey",
               "Trained to a whisper and unshod, so a procession arrives without announcing itself."),
    "Boots":  ("gear_choir_slippers", "Vigil Slippers",
               "Soft-soled. The Choir keeps its vigils barefoot where the ground permits and these where it does not."),
    "Gloves": ("gear_choir_wraps", "Cantor's Wraps",
               "Linen, wound to the second knuckle. A Cantor's hands are for counting time, not for holding."),
})

# ══ ORANGE · SOVEREIGN'S REGALIA ══════════════════════════════════════════════════════════════
# The Gauntlet's institutional set. Status made wearable, per the Sovereign's Tithe-Mark's own note.
NEW += build("set_sovereign", "Orange",
             {"Head": 2, "Neck": -2, "Torso": -4, "Ring1": 4, "Ring2": 3, "Gloves": 3, "Boots": -3},
             mount_proc=0.08, mount_procpct=1.4, rows={
    "Head":   ("gear_sovereign_diadem", "Sovereign's Diadem",
               "Worn by whoever currently holds the Dragon bracket. It is returned, always, and never willingly."),
    "Neck":   ("gear_sovereign_collar", "Collar of the Founding Pact",
               "Names the Gauntlet's first tithe in a script the tournament no longer teaches."),
    "Torso":  ("gear_sovereign_cuirass", "Regalia Cuirass",
               "Ceremonial in cut and emphatically not in construction. The Gauntlet has always been honest about that."),
    "Ring1":  ("gear_sovereign_signet", "Sovereign's Signet",
               "Opens the Gauntlet's inner registry. What is written there is the pact's other half."),
    "Ring2":  ("gear_sovereign_band", "Tithe-Band",
               "One notch per climb paid. Nobody has found the ring where the notches run out."),
    "Mount":  ("gear_sovereign_wyrm", "Sovereign's Wyrm",
               "Not a gift. A loan, from an institution that has never once explained its terms."),
    "Boots":  ("gear_sovereign_greaves", "Regalia Greaves",
               "Made to be seen on a stair. The Eternal Stair, specifically, and the Gauntlet knows it."),
    "Gloves": ("gear_sovereign_gauntlets", "Sovereign's Gauntlets",
               "The Gauntlet's namesake, and the only pair the tournament has ever formally issued."),
})

# ══ ORANGE · THE STONED DEVIL ═════════════════════════════════════════════════════════════════
# The owner's farewell to Dawn's Drunken Angel: the same joke from the other end. An archdevil who
# read the whole prophecy, thought about it properly, and opted out. Dry, not loud — he is sincere,
# which is what makes it funny; the game around him stays straight-faced.
NEW += build("set_stoned_devil", "Orange",
             {"Head": -4, "Neck": 3, "Torso": -3, "Ring1": 5, "Ring2": 4, "Gloves": 2, "Boots": -4},
             mount_proc=0.11, mount_procpct=1.1, rows={
    "Head":   ("gear_stoned_horns", "Horns of Considered Inaction",
               "Filed blunt by their owner, on purpose, over a long afternoon. He says they kept catching on doorframes. "
               "The Choir has written four papers on what else he might have meant."),
    "Neck":   ("gear_stoned_censer", "The Perpetual Censer",
               "Lit some time during the Age of Dawn and never once since. Whatever is in it, there is still some left."),
    "Torso":  ("gear_stoned_robe", "Robe of the Long Sabbatical",
               "Cut for a being who intended to sit down and has now been sitting down for an Age. Remarkably comfortable. "
               "Alarmingly hard to damage."),
    "Ring1":  ("gear_stoned_signet", "Signet of Declined Ascendancy",
               "He was offered a throne. He read the terms, asked two questions nobody could answer, and went back inside."),
    "Ring2":  ("gear_stoned_band", "Band of the Fourth Reconsideration",
               "There were three earlier ones. He is not looking for them."),
    "Mount":  ("gear_stoned_goat", "Extremely Relaxed Hell-Goat",
               "It will carry you anywhere at exactly one speed. Attempts to hurry it have never once succeeded and, "
               "by every account, have never once been forgiven."),
    "Boots":  ("gear_stoned_slippers", "Slippers of the Unwalked Path",
               "Immaculate. Not a scuff on them. That is the joke and it is also the point."),
    "Gloves": ("gear_stoned_mitts", "Mitts of Amiable Menace",
               "He shakes hands. It is worse than the alternative and takes considerably longer."),
})

# ── SetId backfill for the sets that already shipped ────────────────────────────────────────────
# The six Armory relics stay setless ON PURPOSE: Master Canon XIX says they do not match and were
# never meant to be owned together. gear_oathsteel_helm is likewise a lone piece.
BACKFILL = {
    "set_conscript":  ["gear_conscript_helm", "gear_conscript_collar", "gear_conscript_chest",
                       "gear_iron_ring", "gear_worn_band", "gear_draft_horse",
                       "gear_conscript_boots", "gear_conscript_gloves"],
    "set_weir":       ["gear_weir_kettle_helm", "gear_weir_gorget", "gear_weir_brigandine",
                       "gear_causeway_seal", "gear_marchwarden_band", "gear_weir_courser",
                       "gear_weir_marchboots", "gear_weir_handguards"],
    "set_relay":      ["gear_relay_hood", "gear_relay_torc", "gear_relay_coat", "gear_lamp_seal",
                       "gear_vaultkeeper_band", "gear_relay_charger", "gear_relay_treads",
                       "gear_relay_grips"],
    "set_sable_vein": ["gear_sable_circlet", "gear_sable_collar", "gear_sable_mantle",
                       "gear_sable_seal", "gear_sable_band", "gear_sable_courser",
                       "gear_sable_treads", "gear_sable_grips"],
    "set_pano":       ["gear_pano_helm", "gear_pano_amulet", "gear_pano_cuirass", "gear_pano_signet",
                       "gear_pano_band", "gear_pano_steed", "gear_pano_greaves",
                       "gear_pano_gauntlets"],
}


def main():
    p = CONTENT / "gear.json"
    data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    have = {g["id"] for g in data}

    by_id = {g["id"]: g for g in data}
    filled = 0
    for set_id, ids in BACKFILL.items():
        for gid in ids:
            if gid in by_id:
                by_id[gid]["setId"] = set_id
                filled += 1
    for g in data:
        g.setdefault("setId", None)

    added = [g for g in NEW if g["id"] not in have]
    data.extend(added)

    io.open(p, "w", encoding="utf-8", newline="\n").write(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    sets = collections.Counter(g["setId"] for g in data if g.get("setId"))
    setless = [g["id"] for g in data if not g.get("setId")]
    print("gear: +%d new (total %d) · SetId backfilled onto %d shipped pieces" % (len(added), len(data), filled))
    print("  sets: %s" % dict(sorted(sets.items())))
    print("  setless on purpose (%d): %s" % (len(setless), setless))


if __name__ == "__main__":
    main()
