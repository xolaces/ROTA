#!/usr/bin/env python3
"""Wave 1 of the item catalogue: reagents, draughts, stat bags, and the Black Archive sigils.

WHY THIS EXISTS. items.json shipped 114 rows, of which 100 were sigils. The entire
non-sigil catalogue was 5 materials, 2 stat bags and 7 potions -- so 134 of the 139 quest
nodes had nothing to give, and crafting had almost nothing to consume.

THE ONE RULE THIS WAVE IS BUILT AROUND. Reagents come in two lines that never overlap:

    the ROAD line   -- quest drops, bought with ENERGY
    the FIELD line  -- raid threshold drops, bought with STAMINA

Every capstone recipe needs one of each. That is the structural reason a player cannot
pour everything into one pool and still finish a set: the two lines are not substitutes,
and no amount of energy buys a Field reagent. Splits stay a preference, never a solution.

Rarity follows the shipped ladder (Grey/White floor -> Green -> Blue -> Purple -> Orange
ceiling). Nothing here exceeds Orange; Orange is permanent.
"""
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"


def base(id, name, desc, rarity, type_, **kw):
    d = {
        "id": id, "name": name, "description": desc, "rarity": rarity, "type": type_,
        "artKey": id, "statPointsOnUse": 0, "isCraftingIngredient": False,
        "summonRaidId": None, "summonDifficulty": None, "summonSize": None,
        "upgradesTo": None, "restoreResourceType": None, "restoreAmount": 0,
        "restoreToMax": False, "goldPrice": 0,
    }
    d.update(kw)
    return d


def mat(id, name, desc, rarity, upgrades=None):
    return base(id, name, desc, rarity, "Material", isCraftingIngredient=True, upgradesTo=upgrades)


def potion(id, name, desc, rarity, resource, amount, gold, to_max=False):
    return base(id, name, desc, rarity, "Consumable",
                restoreResourceType=resource, restoreAmount=amount, goldPrice=gold,
                restoreToMax=to_max)


def statbag(id, name, desc, rarity, points, upgrades=None):
    return base(id, name, desc, rarity, "StatBag", statPointsOnUse=points, upgradesTo=upgrades)


def sigil(id, name, desc, rarity, raid, difficulty):
    return base(id, name, desc, rarity, "Sigil",
                summonRaidId=raid, summonDifficulty=difficulty, summonSize="Small")


NEW = [
    # == THE ROAD LINE -- quest reagents, one per chapter, paid for in ENERGY ================
    # Each is the thing that chapter's ground is actually made of. A player walking chapter 4
    # will drown in rime-glass and never see a grain of cinder-salt, which is the point.
    mat("mat_causeway_ash", "Causeway Ash",
        "Grey, weightless, and everywhere on the Ashen Causeway. Smiths pack it around a quench "
        "because it takes heat without ever giving it back.",
        "Grey", upgrades="mat_hollow_reed"),
    mat("mat_hollow_reed", "Hollow Marches Reed",
        "Grows only where the ground is too wet to bury anything. The Marches are full of them.",
        "White", upgrades="mat_emberfall_slag"),
    mat("mat_emberfall_slag", "Emberfall Slag",
        "Run-off from a fire that burned in the Age of Dawn and has not entirely stopped. Warm at "
        "the core a thousand years after the fact.",
        "Green", upgrades="mat_keepwall_mortar"),
    mat("mat_keepwall_mortar", "Keepwall Mortar",
        "Prised from a wall Malachar's masons raised in one night. Nobody has explained the speed.",
        "Blue", upgrades="mat_cinder_salt"),
    mat("mat_rime_glass", "Rime-Glass",
        "Frostmere water frozen so slowly it set clear. It does not melt in the hand. It does not "
        "melt in a forge either, which is the difficulty.",
        "Blue"),
    mat("mat_cinder_salt", "Cinder-Salt",
        "Scraped off the Ashen Throne, where the heat drove everything out of the stone but this. "
        "Tastes of iron. Nobody tastes it twice.",
        "Purple"),
    mat("mat_starhollow_dust", "Star Hollow Dust",
        "Collected where the sky is closest and least reliable. It settles upward if left alone, so "
        "it is never left alone.",
        "Purple"),

    # == THE FIELD LINE -- raid reagents, one per grade, paid for in STAMINA =================
    # These come off things that had to be brought down by a company. No quest yields them at
    # any rate, at any difficulty, ever -- that exclusivity is the whole mechanism.
    mat("mat_mire_ichor", "Mire-Ichor",
        "Pale, luminous, foul. Bled from the Brood-things of the drowned shallows. Useless alone; "
        "the basis of half the alchemy on the Drowned Coast.",
        "White", upgrades="mat_brood_chitin"),
    mat("mat_brood_chitin", "Mire-Brood Chitin",
        "Plate from a thing that grows a new one each season and abandons the old where it stood.",
        "Green", upgrades="mat_stag_tendon"),
    mat("mat_stag_tendon", "Ashen Stag Heart-Tendon",
        "The Heartmarch hunters string bows with it and say an arrow loosed from one arrives before "
        "the sound does.",
        "Blue", upgrades="mat_wrathslag"),
    mat("mat_wrathslag", "Wrath-Slag",
        "The cooled residue of a Manifestation. Still faintly warm an Age later, and still, very "
        "slightly, trying to qualify whoever holds it.",
        "Purple", upgrades="mat_colossus_filament"),
    mat("mat_leviathan_baleen", "Leviathan Baleen",
        "Cut from a Primordial that was never killed, only out-waited. It filters things out of air "
        "that air was not known to contain.",
        "Purple"),
    mat("mat_colossus_filament", "Colossus Filament",
        "Sigil-wire from the Iron Colossus's commanding core. Still carrying an order. Still trying "
        "to deliver it to a chain of command four hundred years dead.",
        "Orange"),

    # == THE DEEP REAGENTS -- vanishingly rare, and each one is a canon object ===============
    # These do not sit on a ladder and they do not upgrade into anything. They are the tail of
    # the drop tables: a player may finish the campaign having seen none of them.
    mat("mat_sounding_horn", "Sounding-Horn of the Sunken Leviathan",
        "It still holds one note. The Old Guard who took it never agreed on what the note does, "
        "only that the sea answered the one time it was sounded.",
        "Orange"),
    mat("mat_gravewend_haft", "Haft of Gravewend",
        "A farm tool from a village whose name is not written down, carried by a man whose name is "
        "not written down, who used it to kill a thing that should have killed him. The village is "
        "gone. The pitchfork is not.",
        "Orange"),
    mat("mat_null_sigil_ink", "Null-Sigil Ink",
        "The medium the Sealwrights wrote closure in. It does not dry so much as decide to stop.",
        "Orange"),
    mat("mat_quiet_lamp_oil", "Quiet Lamp Oil",
        "Drawn from a Last Watch relay lamp that has burned unattended since the fall. The Watch "
        "does not know what it burns and has stopped asking.",
        "Orange"),

    # == DRAUGHTS -- the upper rungs of the flat ladder ======================================
    # Priced off the shipped rate (~158 gold per point at the Minor/Major rungs) with a bulk
    # discount that widens as the rung climbs, so buying up is worth doing and gold has somewhere
    # large to go. NOTE: a PERCENTAGE tier is a separate, still-open owner decision -- these are
    # flat, like everything else that ships today.
    potion("potion_energy_greater", "Greater Energy Draught",
           "Restores 150 Energy. Brewed at the relays, for a road that does not end at dusk.",
           "Purple", "Energy", 150, 22000),
    potion("potion_energy_grand", "Grand Energy Draught",
           "Restores 400 Energy. The Weir issues these to couriers who are not expected back soon.",
           "Purple", "Energy", 400, 54000),
    potion("potion_stamina_greater", "Greater Stamina Draught",
           "Restores 150 Stamina. The arm gives out before the will does. This is for the arm.",
           "Purple", "Stamina", 150, 22000),
    potion("potion_stamina_grand", "Grand Stamina Draught",
           "Restores 400 Stamina. Drunk before a company commits to something it cannot walk away from.",
           "Purple", "Stamina", 400, 54000),
    potion("potion_health_greater", "Greater Healing Poultice",
           "Restores 150 Health. Closes what a company opened.",
           "Purple", "Health", 150, 17000),
    potion("elixir_stamina_restoration", "Ancient's Draught",
           "Refills your Stamina completely. Found, never sold.",
           "Purple", "Stamina", 0, 0, to_max=True),

    # == STAT BAGS -- two rungs above Major =================================================
    statbag("statbag_greater", "Greater Stat Bag",
            "Contains 40 unassigned skill points. Old Guard requisition, unopened since the fall.",
            "Purple", 40, upgrades="statbag_ancient"),
    statbag("statbag_ancient", "Ancient's Reliquary",
            "Contains 100 unassigned skill points. What the Dawnward Choir calls an answer.",
            "Orange", 100),

    # == SIGILS -- Black Archive, chapter 7 ==================================================
    # These exist because q_lastwatch_boss ("The Last Lamp of Arveth") declares a sigil drop
    # chance with no sigils map, so it can never drop one -- the content validator refuses the
    # pack over it. Its matching raid (raid_lastwatch_lamp) already ships.
    sigil("sigil_lastwatch_lamp_normal", "Sigil of the Last Lamp",
          "The name of a lamp that has not gone out. Speak it and the vigil resumes.",
          "Green", "raid_lastwatch_lamp", "Normal"),
    sigil("sigil_lastwatch_lamp_hard", "Sigil of the Last Lamp (Hard)",
          "The name of a lamp that has not gone out. Spoken harder, it answers harder.",
          "Blue", "raid_lastwatch_lamp", "Hard"),
    sigil("sigil_lastwatch_lamp_legendary", "Sigil of the Last Lamp (Legendary)",
          "The name of a lamp that has not gone out, and of the thing that has been circling it.",
          "Purple", "raid_lastwatch_lamp", "Legendary"),
    sigil("sigil_lastwatch_lamp_nightmare", "Sigil of the Last Lamp (Nightmare)",
          "The name of a lamp that has not gone out. At this pitch, something else uses the name too.",
          "Orange", "raid_lastwatch_lamp", "Nightmare"),
]


def main():
    p = CONTENT / "items.json"
    data = json.loads(p.read_text(encoding="utf-8"))
    have = {x["id"] for x in data}
    added = [x for x in NEW if x["id"] not in have]
    if not added:
        print("items: nothing to add")
        return
    data.extend(added)
    p.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print("items: +" + str(len(added)) + " (total " + str(len(data)) + ")")


if __name__ == "__main__":
    main()
