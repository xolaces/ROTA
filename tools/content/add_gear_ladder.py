#!/usr/bin/env python3
"""Adds the missing Green/Blue/Purple gear ladder plus the Armory relics.

WHY THIS EXISTS. gear.json shipped with 8 Grey pieces, 1 Blue, and 8 Orange. A player had a
full set at level 1 and nothing else to want until Pano's set, which is the end of the game.
Chapters 2 through 6 -- the overwhelming majority of the content -- dropped no gear at all.

The ladder below fills that, and it is deliberately split across BOTH resources: every tier
has quest-sourced pieces (energy) and raid-sourced pieces (stamina), so neither pool is the
"correct" one to pour points into. The crafted capstones need one reagent from each line,
which is the only place in the game that structurally rewards a mixed build.

Power curve, read off what already shipped (Grey ~1 total, Blue ~10, Orange ~30):
    Grey 1  ->  Green 4  ->  Blue 10  ->  Purple 18  ->  Orange 30
Mounts carry ~3x a body slot, matching gear_pano_steed's 45/45 against the set's 15/15.
"""
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"


def g(id, name, desc, rarity, slot, atk, dfn, icon, proc=None, procpct=None, upgrades=None):
    d = {
        "id": id, "name": name, "description": desc, "rarity": rarity, "slot": slot,
        "bonusAttack": atk, "bonusDefense": dfn,
        "procChance": proc, "procPercent": procpct,
        "iconPath": "icons/gear/" + icon + ".png",
    }
    if upgrades:
        d["upgradesTo"] = upgrades
    return d


NEW = [
    # -- GREEN: the Iron Weir's frontier issue. Chapters 1-2. --------------------------------
    # The Weir does not make beautiful things. It makes things that come back.
    g("gear_weir_kettle_helm", "Weir Kettle Helm",
      "Frontier issue. The brim is wide because rain ruins a bowstring faster than an enemy does.",
      "Green", "Head", 1, 3, "weir_kettle_helm", upgrades="gear_relay_hood"),
    g("gear_weir_gorget", "Weir Gorget",
      "Plate at the throat and nowhere else. The Weir learned which wounds end a watch.",
      "Green", "Neck", 1, 3, "weir_gorget", upgrades="gear_relay_torc"),
    g("gear_weir_brigandine", "Weir Brigandine",
      "Riveted from the scrap of three older coats. Every plate in it has already survived something.",
      "Green", "Torso", 2, 2, "weir_brigandine", upgrades="gear_relay_coat"),
    g("gear_causeway_seal", "Causeway Seal Ring",
      "Ash-pitted signet of a road warden. The Ashen Causeway still has wardens. They are just not paid.",
      "Green", "Ring1", 3, 1, "causeway_seal", upgrades="gear_lamp_seal"),
    g("gear_marchwarden_band", "Marchwarden's Band",
      "Worn thin at one edge, where a thumb rubbed through forty years of standing still.",
      "Green", "Ring2", 3, 1, "marchwarden_band", upgrades="gear_vaultkeeper_band"),
    g("gear_weir_courser", "Weir Courser",
      "Bred for the courier roads. Not fast. Tireless, which on the frontier is the same as fast.",
      "Green", "Mount", 6, 6, "weir_courser", proc=0.05, procpct=2.5,
      upgrades="gear_relay_charger"),
    g("gear_weir_marchboots", "Weir Marchboots",
      "Resoled more times than made. The Weir counts a boot's age in roads, not years.",
      "Green", "Boots", 2, 2, "weir_marchboots", upgrades="gear_relay_treads"),
    g("gear_weir_handguards", "Weir Handguards",
      "Cut long at the wrist. A frontier warden reaches into places they cannot see.",
      "Green", "Gloves", 3, 1, "weir_handguards", upgrades="gear_relay_grips"),

    # -- BLUE: the Last Watch's relay kit. Chapters 3-4. -------------------------------------
    # Everything the Watch carries is meant to keep a signal alive after its carrier is dead.
    g("gear_relay_hood", "Relaykeeper's Hood",
      "Oiled against frost, hemmed in lamp-black. Worn by the ones who kept the dead lamps burning.",
      "Blue", "Head", 3, 7, "relay_hood", upgrades="gear_sable_circlet"),
    g("gear_relay_torc", "Torc of the Quiet Wind",
      "Cold iron, warm at the throat. The Watch says it hums a half-beat before a relay fails.",
      "Blue", "Neck", 5, 5, "relay_torc", upgrades="gear_sable_collar"),
    g("gear_relay_coat", "Relay Coat",
      "Long, grey, unremarkable, which is the point. A courier who is looked at twice is a dead courier.",
      "Blue", "Torso", 3, 7, "relay_coat", upgrades="gear_sable_mantle"),
    g("gear_lamp_seal", "Lamplighter's Seal",
      "Proof the bearer may enter a sealed relay. Nine in ten of those relays no longer answer.",
      "Blue", "Ring1", 7, 3, "lamp_seal", upgrades="gear_sable_seal"),
    g("gear_vaultkeeper_band", "Vaultkeeper's Band",
      "Taken from the Sunken Vaults, which the Watch marked as sealed and the water did not.",
      "Blue", "Ring2", 6, 4, "vaultkeeper_band", upgrades="gear_sable_band"),
    g("gear_relay_charger", "Relay Charger",
      "Trained to run a route with no rider. Several still do, on roads no one has walked in an Age.",
      "Blue", "Mount", 15, 15, "relay_charger", proc=0.06, procpct=2.0,
      upgrades="gear_sable_courser"),
    g("gear_relay_treads", "Frostmere Treads",
      "Nailed for ice. Frostmere takes a careless step and keeps it.",
      "Blue", "Boots", 4, 6, "relay_treads", upgrades="gear_sable_treads"),
    g("gear_relay_grips", "Lampwright's Grips",
      "Scorched across the palms. The lamps of the Watch were never meant to be handled cold.",
      "Blue", "Gloves", 6, 4, "relay_grips", upgrades="gear_sable_grips"),

    # -- PURPLE: House Sable Vein. Chapters 5-6. --------------------------------------------
    # The Threnody Houses do not arm soldiers. They equip witnesses, which is worse.
    g("gear_sable_circlet", "Sable Vein Circlet",
      "Archive work. The script around the band is a catalogue number, and the thing catalogued is you.",
      "Purple", "Head", 7, 11, "sable_circlet"),
    g("gear_sable_collar", "Threnody Collar",
      "Worn by a scribe who recorded a Manifestation from close enough to be corrected by it.",
      "Purple", "Neck", 9, 9, "sable_collar"),
    g("gear_sable_mantle", "Mantle of the Sable Vein",
      "Black on black, and the inner black is older. The Houses do not explain their dyes.",
      "Purple", "Torso", 7, 11, "sable_mantle"),
    g("gear_sable_seal", "House Sable Seal",
      "Opens an archive hall that appears on no map the Houses admit to holding.",
      "Purple", "Ring1", 12, 6, "sable_seal"),
    g("gear_sable_band", "Vein-Cut Band",
      "Split by a hairline fracture that has not widened in two hundred years of being watched.",
      "Purple", "Ring2", 11, 7, "sable_band"),
    g("gear_sable_courser", "Sable Courser",
      "Archive stock. It will not cross running water, and the Houses have never said why.",
      "Purple", "Mount", 27, 27, "sable_courser", proc=0.065, procpct=1.6),
    g("gear_sable_treads", "Stair-Worn Boots",
      "Taken off the Eternal Stair. The wear is at the toe, not the heel. Whoever wore them was climbing.",
      "Purple", "Boots", 8, 10, "sable_treads"),
    g("gear_sable_grips", "Archivist's Grips",
      "Thin, precise, reinforced at the fingertips. Some things in an archive must be held down.",
      "Purple", "Gloves", 10, 8, "sable_grips"),

    # -- ORANGE: the Armory. Master Canon XIX. ----------------------------------------------
    # These are canon items made wearable. They are NOT a set: they do not match, they were never
    # meant to be owned together, and a player holding two of them is already unusual. Pano's set
    # remains the only matched suit in the game.
    g("gear_cinder_cuff", "The Cinder-Cuff",
      "Wrath-slag, cooled and cuffed. It makes the wearer stronger and angrier in the same motion, "
      "and does not distinguish between the two.",
      "Orange", "Gloves", 26, 4, "cinder_cuff", proc=0.09, procpct=1.8),
    g("gear_unworn_crown", "The Unworn Crown",
      "A circlet of a dark metal no living smith can name, made for a head no carving depicts. It has "
      "never, in any account, been worn. The Old Guard kept it because they were afraid to be the ones "
      "who found out why.",
      "Orange", "Head", 15, 15, "unworn_crown"),
    g("gear_cold_token", "The Vanguard's Cold Token",
      "Kin to the lost signet: the same unknown script worn nearly smooth, the same cold that does not "
      "warm in the hand. Nobody alive can read it. That is the whole of what is known.",
      "Orange", "Neck", 12, 18, "cold_token"),
    g("gear_colossus_core", "Colossus-Core Shard",
      "Pried from a war-construct that never stood down. It still pulses with the last order it was "
      "given, and there is no one left to amend it: hold.",
      "Orange", "Torso", 6, 34, "colossus_core"),
    g("gear_sovereign_tithe", "Sovereign's Tithe-Mark",
      "Granted by the Gauntlet in the name of the founding pact. It marks a fighter as having paid the "
      "tithe. What the Sovereign was promised is recorded nowhere a fighter may read.",
      "Orange", "Ring1", 20, 10, "sovereign_tithe"),
    g("gear_sealwright_stylus", "The Sealwright's Stylus",
      "The instrument that inscribed null-sigil bindings, the script that holds a thing closed. The Old "
      "Guard made few and accounted for every one. This one is not on the ledger.",
      "Orange", "Ring2", 10, 20, "sealwright_stylus"),
]


def main():
    p = CONTENT / "gear.json"
    data = json.loads(p.read_text(encoding="utf-8"))
    have = {x["id"] for x in data}
    added = [x for x in NEW if x["id"] not in have]
    if not added:
        print("gear: nothing to add")
        return
    data.extend(added)
    p.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print("gear: +" + str(len(added)) + " (total " + str(len(data)) + ")")


if __name__ == "__main__":
    main()
