#!/usr/bin/env python3
"""Wave 2 items: reagents keyed to the new raid tags, draughts, and the Devil's own shelf.

WHAT THIS WAVE IS FOR. Wave 1 built two reagent lines split by RESOURCE — Road reagents from quests
(energy) and Field reagents from raids (stamina). That split is about which pool you spend. This wave
adds a second axis that is about WHAT YOU FIGHT: a reagent per raid tag, dropped by the raids carrying
that tag. The two axes are deliberately independent, so "which pool" and "which enemy" are separate
questions and a crafting recipe can ask both.

It also gives the eight new gear sets something to be made of, and the Stoned Devil a shelf — the
consumables an archdevil on an Age-long sabbatical would plausibly have lying around, played
straight, because he is sincere and that is the joke.

RARITY LADDERS ALL ASCEND STRICTLY and top out at Orange, which is permanent. Boot validation checks
this, and it caught three chains in wave 1 that did not.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"


def base(id_, name, desc, rarity, type_, **kw):
    d = {
        "id": id_, "name": name, "description": desc, "rarity": rarity, "type": type_,
        "artKey": id_, "statPointsOnUse": 0, "isCraftingIngredient": False,
        "summonRaidId": None, "summonDifficulty": None, "summonSize": None,
        "upgradesTo": None, "restoreResourceType": None, "restoreAmount": 0,
        "restoreToMax": False, "goldPrice": 0, "tags": [],
    }
    d.update(kw)
    return d


def mat(id_, name, desc, rarity, tags=None, upgrades=None):
    return base(id_, name, desc, rarity, "Material", isCraftingIngredient=True,
                upgradesTo=upgrades, tags=tags or [])


def potion(id_, name, desc, rarity, resource, amount, gold, to_max=False):
    return base(id_, name, desc, rarity, "Consumable", restoreResourceType=resource,
                restoreAmount=amount, goldPrice=gold, restoreToMax=to_max)


def statbag(id_, name, desc, rarity, points, upgrades=None):
    return base(id_, name, desc, rarity, "StatBag", statPointsOnUse=points, upgradesTo=upgrades)


NEW = [
    # ══ THE TROPHY LINE — one reagent per raid tag, off the raids that carry it ═══════════════
    # This is the second axis: wave 1 split reagents by which POOL pays for them, this splits by
    # WHAT you had to fight. A recipe can now ask for both and mean two different things.
    mat("mat_warren_teeth", "Warren Teeth",
        "Goblins replace them constantly and leave the old ones where they fall. A tunnel floor is "
        "half gravel and half this.",
        "Green", tags=["Goblin"], upgrades="mat_brood_carapace"),
    mat("mat_brood_carapace", "Brood Carapace",
        "Plate off something that outgrew three of these before anyone got close enough to measure.",
        "Blue", tags=["Goblin", "Beast"]),
    mat("mat_stag_ash", "Ashen Stag Ash",
        "What is left where one lay down. The Heartmarch hunters do not collect it and will not say why.",
        "Blue", tags=["Beast"], upgrades="mat_deepwood_heart"),
    mat("mat_deepwood_heart", "Deepwood Heart",
        "Cut from an apex that had been growing since before the Sundering. Still warm four days out.",
        "Purple", tags=["Beast"]),
    mat("mat_gravesalt", "Gravesalt",
        "The Watch packs it around anything it has to move twice. It works, and nobody has asked how.",
        "Green", tags=["Undead"], upgrades="mat_barrow_iron"),
    mat("mat_barrow_iron", "Barrow Iron",
        "Grave-goods iron, buried long enough to take on the habit. It does not rust and it does not "
        "hold an edge — the Watch considers the trade fair.",
        "Purple", tags=["Undead"]),
    mat("mat_amber_rot", "Amber Rot",
        "The Awakening's own residue. It is warm, it is slightly wrong, and it keeps indefinitely in "
        "a sealed jar that nobody wants in their pack.",
        "Blue", tags=["Shadow"], upgrades="mat_glutbound_core"),
    mat("mat_glutbound_core", "Glutbound Core",
        "The dense part of a thing that had nearly finished becoming something else.",
        "Purple", tags=["Shadow", "Demon"]),
    mat("mat_command_sigil", "Cracked Command Sigil",
        "Prised off a Wrought mid-order. It is still trying to finish the sentence.",
        "Blue", tags=["Construct"], upgrades="mat_colossus_filament"),
    mat("mat_brimstone_slag", "Brimstone Slag",
        "Cools where a Swollen thing stood too long. Emberpan is paved in it, mostly.",
        "Blue", tags=["Demon"], upgrades="mat_glutbound_core"),
    mat("mat_leviathan_tooth", "Leviathan Tooth",
        "One of very many, and still the largest object most people will ever hold.",
        "Purple", tags=["Horror"]),
    mat("mat_kronarch_seal", "Kronarch's Broken Seal",
        "From the muster-rolls of the army that won against nothing. Most of the names are legible.",
        "Purple", tags=["Legion"]),
    mat("mat_sovereign_scale", "Sovereign's Shed Scale",
        "The Gauntlet collects them. It has never said what for and has never been asked twice.",
        "Orange", tags=["Dragon"]),

    # ══ SET REAGENTS — what the eight new sets are actually made of ═══════════════════════════
    mat("mat_lamp_black", "Lamp-Black",
        "Soot off a relay lamp, ground fine. The Watch hems its hoods with it so a courier does not "
        "shine in a doorway.",
        "Green", upgrades="mat_sable_thread"),
    mat("mat_sable_thread", "Sable Vein Thread",
        "The Houses dye it twice and will not discuss the second dye.",
        "Purple"),
    mat("mat_pitch_seal", "Marsh Pitch",
        "Boiled down over three days. Gravewardens seal their wades with it and their coffins too.",
        "Green"),
    mat("mat_cork_iron", "Cork-and-Iron Sole",
        "Salvager's stock. Buoyant enough to float a boot and heavy enough to keep it down.",
        "Blue"),
    mat("mat_resonant_brass", "Resonant Brass",
        "Cast to hum at one note and no other. The Choir orders it by the tone, never the weight.",
        "Purple"),
    mat("mat_siege_lead", "Siege Lead",
        "Backing for a breaker's visor. A Wrought does not aim, which makes shielding a guess "
        "everywhere at once.",
        "Blue"),

    # ══ DRAUGHTS — the rungs between Greater and the Ancient's elixirs ════════════════════════
    potion("potion_energy_relay", "Relay Draught",
           "Restores 900 Energy. Issued to a courier who is not expected to stop.",
           "Orange", "Energy", 900, 108000),
    potion("potion_stamina_relay", "Warhorn Draught",
           "Restores 900 Stamina. Drunk by a company that has decided the thing in front of it is going down today.",
           "Orange", "Stamina", 900, 108000),
    potion("potion_health_grand", "Grand Healing Poultice",
           "Restores 400 Health. Field-standard for anything the Weir calls a bad week.",
           "Purple", "Health", 400, 44000),
    potion("elixir_health_restoration", "Ancient's Mending",
           "Refills your Health completely. Found, never sold.",
           "Purple", "Health", 0, 0, to_max=True),

    # ══ STAT BAGS — a rung between Greater and the Reliquary ══════════════════════════════════
    statbag("statbag_oathsworn", "Oathsworn Cache",
            "Contains 65 unassigned skill points. Left by a company that did not need them after all.",
            "Purple", 65, upgrades="statbag_ancient"),

    # ══ THE DEVIL'S SHELF — played straight, which is the joke ════════════════════════════════
    # He is sincere. The game around him stays entirely serious, and that is what makes it land.
    potion("potion_devils_tea", "The Devil's Tea",
           "Restores 260 Energy. He offers it to everyone who comes to kill him. Several have stayed "
           "for a second cup and one is reportedly still there.",
           "Purple", "Energy", 260, 38000),
    potion("potion_long_afternoon", "The Long Afternoon",
           "Restores 260 Stamina. Bottled from a nap of genuinely historic proportions.",
           "Purple", "Stamina", 260, 38000),
    mat("mat_censer_resin", "Perpetual Censer Resin",
        "Scraped from the inside of a censer lit during the Age of Dawn. There is still some left. "
        "There has always been some left.",
        "Orange"),
    mat("mat_unwalked_leather", "Unwalked Leather",
        "Cut for slippers that were never worn outdoors. Immaculate, and faintly reproachful.",
        "Purple"),
    base("statbag_reconsideration", "A Moment's Reconsideration",
         "Grants 25 unassigned skill points. He suggests you think about where you put them. He is "
         "not going to elaborate and he is not going to stop looking at you.",
         "Blue", "StatBag", statPointsOnUse=25),
]


def main():
    p = CONTENT / "items.json"
    data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    have = {i["id"] for i in data}
    for i in data:
        i.setdefault("tags", [])
    added = [i for i in NEW if i["id"] not in have]
    data.extend(added)
    io.open(p, "w", encoding="utf-8", newline="\n").write(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    types = collections.Counter(i["type"] for i in data)
    tagged = collections.Counter(t for i in data for t in (i.get("tags") or []))
    print("items: +%d (total %d)" % (len(added), len(data)))
    print("  by type: %s" % dict(sorted(types.items())))
    print("  reagents by raid tag: %s" % dict(sorted(tagged.items())))


if __name__ == "__main__":
    main()
