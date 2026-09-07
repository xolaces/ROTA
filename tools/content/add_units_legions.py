#!/usr/bin/env python3
"""Twenty-six units and six legions — and an answer for every raid tag a player will meet.

TWO HOLES THIS FILLS.

1. THE ROSTER WAS THREE RACES DEEP. units.json shipped 11 rows using Human, Elf and Undead. Oroc,
   Dwarf, Beast, Construct and Demon existed in the enum and nowhere in the world; the Healer role
   and the Special attribute likewise. That is not a cosmetic gap: a legion's slots can DEMAND a
   race, role or attribute, so a constraint on a thing that does not exist is a slot nobody can
   fill, and the whole slot-constraint mechanic was therefore limited to what happened to be there.

2. FOUR RAID TAGS HAD NO ANSWER. The tag system shipped with legions countering Construct, Legion,
   Shadow, Undead and Goblin. Beast, Demon, Horror and Dragon were tagged on raids and answered by
   nobody, which makes them decoration rather than counter-play.

THE CURVES, read off what already shipped and extended two rungs:

    troop    White 30/20   Green 45/30   Blue 60/45   Purple 90/65   Orange 130/95
    general  Green 80/60   Blue 140/90   Purple 220/130   Orange 340/190
    legionBonus (generals only)   Green 5   Blue 10   Purple 18   Orange 28

LegionConfig.MaxUnitProcBonus caps aggregate unit procs at 5.0, and every ability here is priced
well inside it — the shipped generals run procChance 0.08 x procAmount 0.4, and nothing below is
more than an order of magnitude from that.

AFFINITY NUMBERS ARE DELIBERATELY MODEST (18-32%). A counter-build should be a good decision, not
a required one, and the owner has said they will tune the specific bonuses per legion.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

TROOP = {"White": (30, 20), "Green": (45, 30), "Blue": (60, 45), "Purple": (90, 65), "Orange": (130, 95)}
GENERAL = {"Green": (80, 60), "Blue": (140, 90), "Purple": (220, 130), "Orange": (340, 190)}
LEGION_BONUS = {"Green": 5, "Blue": 10, "Purple": 18, "Orange": 28}


def unit(id_, name, desc, kind, rarity, race, role, attr, acq,
         ability=None, passive=False, gem=0, atk=None, dfn=None):
    table = GENERAL if kind == "General" else TROOP
    a, d = table[rarity]
    return {
        "id": id_, "name": name, "description": desc,
        "unitType": kind, "rarity": rarity,
        "baseAttack": atk if atk is not None else a,
        "baseDefense": dfn if dfn is not None else d,
        "race": race, "role": role, "attribute": attr,
        "ability": ({"procChance": ability[0], "procAmount": ability[1], "conditions": []}
                    if ability else None),
        "isPassive": passive,
        "legionBonus": LEGION_BONUS[rarity] if kind == "General" else 0,
        "iconPath": "icons/unit/" + id_.split("_", 1)[1] + ".png",
        "acquisition": acq, "gemPrice": gem,
    }


UNITS = [
    # ══ OROC — the frontier's other people. Big, deliberate, and not remotely interested in you ══
    unit("gen_gorruk", "Gorruk Stonejaw",
         "An Oroc line-holder who has never once been moved off a position he agreed to hold. "
         "The agreeing is the hard part.",
         "General", "Blue", "Oroc", "Tank", "Strength", "Chapter 3 raid threshold", (0.09, 0.55)),
    unit("gen_makh", "Makh the Unhurried",
         "Fights at exactly one speed. Opponents consistently mistake this for an opening.",
         "General", "Purple", "Oroc", "Melee", "Strength", "Chapter 5 boss drops", (0.11, 0.95)),
    unit("troop_oroc_shieldline", "Oroc Shieldline",
         "They do not advance. That is not a limitation, it is the entire service being offered.",
         "Troop", "Blue", "Oroc", "Tank", "Strength", "Chapter 3 quest drops"),
    unit("troop_oroc_maulers", "Oroc Maulers",
         "Recruited by the Weir at rates it does not put in writing.",
         "Troop", "Purple", "Oroc", "Melee", "Strength", "Chapter 5 quest drops"),

    # ══ DWARF — the Iron Weir's engineers, and the only people who still break Wrought ══════════
    unit("gen_durn", "Durn Anvilkeep",
         "Reads a construct's commanding sigil the way other people read weather. Wrong twice; "
         "he keeps both notes.",
         "General", "Blue", "Dwarf", "Special", "Intellect", "Chapter 3 raid threshold", (0.07, 0.70)),
    unit("gen_brannoc", "Brannoc Deepvein",
         "The Weir's siege-master. Has taken down four Wrought and will discuss none of them.",
         "General", "Purple", "Dwarf", "Tank", "Strength", "Chapter 5 raid threshold", (0.10, 1.05)),
    unit("troop_dwarf_sappers", "Weir Sappers",
         "They go under the thing. Frontier doctrine has never improved on this.",
         "Troop", "Blue", "Dwarf", "Special", "Intellect", "Chapter 4 quest drops"),
    unit("troop_dwarf_hammers", "Anvilkeep Hammers",
         "Issued one hammer and one instruction, both heavy.",
         "Troop", "Purple", "Dwarf", "Melee", "Strength", "Chapter 5 quest drops"),

    # ══ BEAST — Order III, taken alive by people who should have known better ═══════════════════
    unit("gen_ashen_stag", "The Ashen Stag",
         "Not tamed. Accompanying. The Heartmarch hunters are precise about the distinction and "
         "have been since the first one tried the other word.",
         "General", "Purple", "Beast", "Special", "Agility", "Chapter 4 boss drops", (0.13, 0.80)),
    unit("troop_mire_brood", "Mire-Brood Swarm",
         "Bled for reagent, herded for war. Neither use was the Brood's idea.",
         "Troop", "Green", "Beast", "Melee", "Agility", "Chapter 2 quest drops"),
    unit("troop_rime_wolves", "Rimewood Wolves",
         "They hunt the cold better than anything the Watch has ever fielded, and they know it.",
         "Troop", "Blue", "Beast", "Ranged", "Agility", "Chapter 4 quest drops"),
    unit("troop_glacier_bears", "Glacier-Maw Bears",
         "Taken as cubs from a den nobody has found twice.",
         "Troop", "Purple", "Beast", "Tank", "Strength", "Chapter 4 boss drops"),

    # ══ CONSTRUCT — Wrought that were restarted, which the Weir insists is different ════════════
    unit("gen_sentinel_prime", "Sentinel Prime",
         "Given a new order by someone with no authority to give it. It has not noticed, or it has "
         "and does not care, and the Weir has stopped asking which.",
         "General", "Purple", "Construct", "Tank", "Special", "Iron Colossus", (0.08, 1.20), passive=False),
    unit("troop_wrought_automata", "Salvaged Automata",
         "Restarted, roughly. They execute the order and nothing else, including stopping.",
         "Troop", "Blue", "Construct", "Melee", "Special", "Chapter 3 raid threshold"),
    unit("troop_lamp_walkers", "Lamp-Walkers",
         "Relay-work that kept walking its route after the relay fell. The Watch marches beside them now.",
         "Troop", "Purple", "Construct", "Ranged", "Special", "Black Archive"),

    # ══ DEMON — the Swollen, on a leash of debatable quality ════════════════════════════════════
    unit("gen_vaskarr", "Vaskarr the Bargained",
         "Bound by an agreement the Choir drafted and the Threnody Houses will not read aloud.",
         "General", "Purple", "Demon", "Special", "Wisdom", "Chapter 5 raid threshold", (0.12, 1.10)),
    unit("gen_the_reconsidered", "The Reconsidered",
         "An archdevil's aide, sent along on the understanding that he would 'have a look'. He has "
         "had a look. He is still here. Nobody has raised it.",
         "General", "Orange", "Demon", "Special", "Special", "The Stoned Devil", (0.09, 2.10)),
    unit("troop_emberpan_imps", "Emberpan Imps",
         "Malicious, tireless, and extremely literal about instructions.",
         "Troop", "Blue", "Demon", "Ranged", "Intellect", "Chapter 5 quest drops"),
    unit("troop_slagborn", "Slagborn",
         "What cools where a Manifestation stood, if it cools into legs.",
         "Troop", "Purple", "Demon", "Melee", "Strength", "Chapter 5 boss drops"),

    # ══ HEALER — the role that existed only in the enum ═════════════════════════════════════════
    unit("gen_sister_arveth", "Sister Arveth of the Lamp",
         "The lamp is named for her, not the other way round. She would like that corrected and "
         "the Watch has declined for two hundred years.",
         "General", "Purple", "Human", "Healer", "Wisdom", "Black Archive", (0.15, 0.60), passive=True),
    unit("gen_choirmaster", "The Dawnward Choirmaster",
         "Keeps a company standing by counting time at them. It should not work.",
         "General", "Orange", "Human", "Healer", "Wisdom", "Chapter 6 boss drops", (0.14, 1.40), passive=True),
    unit("troop_field_chirurgeons", "Field Chirurgeons",
         "Weir-trained, which means fast, unsentimental, and usually right.",
         "Troop", "Green", "Human", "Healer", "Intellect", "Chapter 2 quest drops"),
    unit("troop_choir_attendants", "Choir Attendants",
         "They stand and do not sing. Somebody has to be listening to the listeners.",
         "Troop", "Blue", "Human", "Healer", "Wisdom", "Chapter 6 quest drops"),

    # ══ THE TOP OF THE LADDER ══════════════════════════════════════════════════════════════════
    unit("gen_pano", "Pano, the Lost Vanguard",
         "The banner came back. Nobody has ever explained the rest of it, and the Watch has stopped "
         "asking in writing.",
         "General", "Orange", "Human", "Melee", "Strength", "Pano's set completion", (0.16, 1.90)),
    unit("troop_vanguard_oathsworn", "Vanguard Oathsworn",
         "They swore to a man who did not come back, and have not considered that a release.",
         "Troop", "Orange", "Human", "Melee", "Strength", "Chapter 6 raid threshold"),
    unit("troop_sable_witnesses", "Sable Vein Witnesses",
         "House scribes who record a fight from inside it. Their accounts are the only ones that agree.",
         "Troop", "Orange", "Human", "Special", "Intellect", "Black Archive"),
]

LEGIONS = [
    # Answers for the four tags nobody countered, plus a Goblin specialist so chapter 2 has one.
    {
        "id": "legion_houndsmen", "name": "The Houndsmen",
        "description": "A hunting company, not an army. Built around beasts and the people who can "
                       "stand near them, it goes where a formation cannot and arrives sooner.",
        "rarity": "Blue", "powerBonus": 110,
        "generals": [("Race", "Beast"), ("None", None)],
        "troops": [("Attribute", "Agility"), ("None", None), ("None", None)],
        "affinities": {"Beast": 28.0, "Goblin": 18.0},
        "acquisition": "Chapter 4 boss drops", "gem": 0,
    },
    {
        "id": "legion_warrenguard", "name": "The Warrenguard",
        "description": "Iron Weir tunnel work. Short ranks, low ceilings, and a doctrine that assumes "
                       "the enemy is already inside the line.",
        "rarity": "Green", "powerBonus": 70,
        "generals": [("Role", "Tank"), ("None", None)],
        "troops": [("None", None), ("None", None)],
        "affinities": {"Goblin": 26.0},
        "acquisition": "Chapter 2 quest drops", "gem": 0,
    },
    {
        "id": "legion_anvilkeep", "name": "The Anvilkeep Siege",
        "description": "Dwarven engineers and the heaviest thing they could get to the site. It is "
                       "slow, it is loud, and Wrought do not get back up.",
        "rarity": "Purple", "powerBonus": 240,
        "generals": [("Race", "Dwarf"), ("Role", "Tank"), ("None", None)],
        "troops": [("Attribute", "Strength"), ("Attribute", "Strength"), ("None", None)],
        "affinities": {"Construct": 32.0, "Horror": 18.0},
        "acquisition": "Chapter 5 raid threshold", "gem": 0,
    },
    {
        "id": "legion_bargained", "name": "The Bargained Company",
        "description": "Demons under written agreement, fielded by people who have read the agreement "
                       "very carefully. It answers its own kind better than anything else will.",
        "rarity": "Purple", "powerBonus": 230,
        "generals": [("Race", "Demon"), ("Attribute", "Wisdom"), ("None", None)],
        "troops": [("None", None), ("None", None), ("None", None)],
        "affinities": {"Demon": 30.0, "Shadow": 18.0},
        "acquisition": "Chapter 5 boss drops", "gem": 0,
    },
    {
        "id": "legion_deepwatch", "name": "The Deepwatch",
        "description": "The Last Watch's answer to the things that were here first. Everyone in it has "
                       "seen one and elected to come back, which is the only entry requirement.",
        "rarity": "Orange", "powerBonus": 320,
        "generals": [("Role", "Healer"), ("None", None), ("None", None)],
        "troops": [("None", None), ("None", None), ("None", None), ("None", None)],
        "affinities": {"Horror": 30.0, "Undead": 22.0},
        "acquisition": "Black Archive", "gem": 0,
    },
    {
        "id": "legion_sovereigns_climb", "name": "The Sovereign's Climb",
        "description": "A Gauntlet formation, fielded under the founding pact. What it costs to raise "
                       "is written in the inner registry, which fighters may not read.",
        "rarity": "Orange", "powerBonus": 340,
        "generals": [("None", None), ("None", None), ("None", None)],
        "troops": [("None", None), ("None", None), ("None", None), ("None", None)],
        "affinities": {"Dragon": 30.0, "Legion": 20.0},
        "acquisition": "Gauntlet rank prize", "gem": 0,
    },
]


def slots(spec):
    return [{"constraintType": t, "constraintValue": v} for t, v in spec]


def main():
    up, lp = CONTENT / "units.json", CONTENT / "legions.json"
    units = json.load(io.open(up, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    legions = json.load(io.open(lp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)

    have_u = {u["id"] for u in units}
    added_u = [u for u in UNITS if u["id"] not in have_u]
    units.extend(added_u)

    have_l = {l["id"] for l in legions}
    added_l = []
    for spec in LEGIONS:
        if spec["id"] in have_l:
            continue
        added_l.append({
            "id": spec["id"], "name": spec["name"], "description": spec["description"],
            "rarity": spec["rarity"], "powerBonus": spec["powerBonus"],
            "generalSlots": slots(spec["generals"]), "troopSlots": slots(spec["troops"]),
            "iconPath": "icons/legion/" + spec["id"].replace("legion_", "") + ".png",
            "acquisition": spec["acquisition"], "gemPrice": spec["gem"],
            "tagAffinities": spec["affinities"],
        })
    legions.extend(added_l)

    io.open(up, "w", encoding="utf-8", newline="\n").write(
        json.dumps(units, indent=2, ensure_ascii=False) + "\n")
    io.open(lp, "w", encoding="utf-8", newline="\n").write(
        json.dumps(legions, indent=2, ensure_ascii=False) + "\n")

    print("units: +%d (total %d) · legions: +%d (total %d)"
          % (len(added_u), len(units), len(added_l), len(legions)))
    print("  races:      %s" % dict(collections.Counter(u["race"] for u in units).most_common()))
    print("  roles:      %s" % dict(collections.Counter(u["role"] for u in units).most_common()))
    print("  attributes: %s" % dict(collections.Counter(u["attribute"] for u in units).most_common()))
    answered = collections.Counter(
        t for l in legions for t in (l.get("tagAffinities") or {}))
    print("  tags answered by a legion: %s" % dict(sorted(answered.items())))


if __name__ == "__main__":
    main()
