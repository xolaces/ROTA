#!/usr/bin/env python3
"""Twenty-four magics, and a category spread that is not 82% Damage.

WHAT WAS WRONG. magics.json shipped 17 rows: 14 Damage, 1 Crit, 1 Gold, 1 Leveling, 0 Utility. Five
of the Damage rows are the inert Orange pinnacle placeholders (procChance 0, procAmount 0), so the
PLAYABLE catalogue was twelve, of which nine did the same thing. A player equipping five magics was
choosing five flavours of "more damage".

THE ARITHMETIC THE VALUES SIT ON. A magic's expected contribution is procChance x procAmount, and
MagicConfig.MaxAggregateProcBonus caps the sum of five equipped at 5.0. The shipped magics run
EV 0.03 to 0.064 each, so five of them reach ~0.25 — the cap is nowhere near binding, and it is not
the thing that balances a loadout. What balances a loadout is that the five slots are scarce. So
these are priced on a rarity ladder rather than against the cap:

    White  ~0.02   Green ~0.04   Blue ~0.07   Purple ~0.11   Orange ~0.17

and the SHAPE varies inside each rung, which is where the choosing happens: a steady 100%-chance
trickle and a 2%-chance windfall can carry the same EV and feel nothing alike.

UTILITY IS A SHAPE, NOT A NEW EFFECT. There are only four effect types (DamageProc, CritChanceFlat,
GoldProc, XpProc) and adding a fifth is a code change nobody asked for. So Utility here means the
magics whose DISTRIBUTION is the point — the lottery tickets and the metronomes — rather than a new
mechanic wearing a category name.

THE FIVE PINNACLE PLACEHOLDERS ARE LEFT INERT ON PURPOSE. They are gated on pinnacle levels the owner
has not sized yet (see LevelingConfig.PinnacleGemRewards, where 2000/15000/25000 are still omitted
pending confirmation). Giving them values here would be deciding that.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"


def magic(id_, name, desc, rarity, category, effect, chance, amount, gem, acq, stacks=True):
    return {
        "id": id_, "name": name, "description": desc, "rarity": rarity,
        "category": category, "effectType": effect,
        "procChance": chance, "procAmount": amount,
        "conditions": [], "stacks": stacks,
        "iconPath": "icons/magic/" + id_.replace("magic_", "") + ".png",
        "acquisition": acq, "gemPrice": gem,
    }


NEW = [
    # ══ DAMAGE — the rungs the ladder was missing, not more of the same ═══════════════════════
    magic("magic_hollowpoint", "Hollow Point",
          "A round bored out and left empty. What fills it on the way in is the argument.",
          "Green", "Damage", "DamageProc", 0.05, 0.80, 20, "Quest drops, chapter 2+"),
    magic("magic_rimefang", "Rimefang",
          "Frostmere ice that never gave up being water. It finds the gap in a thing and then widens it.",
          "Blue", "Damage", "DamageProc", 0.12, 0.58, 60, "Quest drops, chapter 4+"),
    magic("magic_sunder", "Sunder",
          "Old Guard siege doctrine, reduced to one word and one motion.",
          "Purple", "Damage", "DamageProc", 0.09, 1.20, 140, "Raid threshold, Deadly+"),
    magic("magic_wrathslag_ember", "Wrathslag Ember",
          "A coal off a Manifestation. Still warm, still trying, still not quite a Herald.",
          "Purple", "Damage", "DamageProc", 0.14, 0.78, 150, "Chapter 5 boss drops"),
    magic("magic_kronarchs_last_order", "Kronarch's Last Order",
          "The command he gave when the tide went out and nothing answered. It still lands.",
          "Orange", "Damage", "DamageProc", 0.06, 2.85, 0, "Black Archive"),

    # ══ CRIT — one shipped. CritChanceFlat is always-on, so procChance is ignored ═════════════
    magic("magic_keen_eye", "Keen Eye",
          "The Watch teaches it before it teaches anything else: look at the seam, not the shield.",
          "White", "Crit", "CritChanceFlat", 0.0, 0.02, 8, "Starting shop"),
    magic("magic_readers_mark", "Reader's Mark",
          "House Sable Vein trains a scribe to find the one line that matters. It transfers.",
          "Blue", "Crit", "CritChanceFlat", 0.0, 0.08, 65, "Quest drops, chapter 3+"),
    magic("magic_veiled_eye", "The Veiled Eye",
          "Discernment's own nature, briefly lent. It does not see everything. It sees the thing that matters.",
          "Purple", "Crit", "CritChanceFlat", 0.0, 0.14, 175, "Mastery: Discernment"),
    magic("magic_sealwrights_measure", "The Sealwright's Measure",
          "Before you can close a thing you must know exactly where it opens.",
          "Orange", "Crit", "CritChanceFlat", 0.0, 0.22, 0, "Sunken Vaults, vanishingly rare"),

    # ══ GOLD — one shipped, and gold is the only currency with a real sink ════════════════════
    magic("magic_coinsense", "Coin-Sense",
          "A caravan lord's habit, distilled. It notices what a room is worth on the way in.",
          "White", "Gold", "GoldProc", 0.04, 0.60, 8, "Starting shop"),
    magic("magic_tithe_ledger", "Tithe Ledger",
          "The Gauntlet has kept one since its founding. Nobody has audited it and nobody has offered.",
          "Green", "Gold", "GoldProc", 0.06, 0.90, 30, "Gauntlet token shop"),
    magic("magic_hoarders_eye", "Hoarder's Eye",
          "Hoard's dominion is not petty. It is the principle of accumulation itself, and it is contagious.",
          "Blue", "Gold", "GoldProc", 0.08, 1.30, 70, "Mastery: Hoard"),
    magic("magic_spoils_of_the_march", "Spoils of the March",
          "What an army leaves is worth more than what it carried. The Weir has always known this.",
          "Purple", "Gold", "GoldProc", 0.10, 2.20, 160, "Raid threshold, Elite+"),
    magic("magic_sovereigns_cut", "The Sovereign's Cut",
          "Every climb pays a tithe. This is the arrangement that decides which way it flows.",
          "Orange", "Gold", "GoldProc", 0.12, 3.40, 0, "Gauntlet rank prize"),

    # ══ LEVELING — one shipped, and it is the White rung ══════════════════════════════════════
    magic("magic_drillmasters_cant", "Drillmaster's Cant",
          "The Weir's marching count. Nothing about it is magical and it works anyway.",
          "Green", "Leveling", "XpProc", 0.05, 2.40, 30, "Quest drops, chapter 2+"),
    magic("magic_field_commission", "Field Commission",
          "Promotion granted where it was earned, by whoever was still standing to grant it.",
          "Blue", "Leveling", "XpProc", 0.07, 3.00, 75, "Raid threshold, Deadly+"),
    magic("magic_the_long_watch", "The Long Watch",
          "Four hundred years of standing somewhere, compressed into the part that teaches.",
          "Purple", "Leveling", "XpProc", 0.09, 4.20, 170, "Black Archive"),
    magic("magic_age_of_dawn_fragment", "Fragment of the Age of Dawn",
          "A piece of a lesson from before the Sundering. The rest of it is not recoverable.",
          "Orange", "Leveling", "XpProc", 0.10, 6.00, 0, "Chapter 6 boss drops"),

    # ══ UTILITY — the shapes, not a new mechanic ══════════════════════════════════════════════
    # These carry ordinary EV in extraordinary distributions. A metronome and a lottery ticket can
    # be worth the same on average and feel nothing alike, and that is the whole category.
    magic("magic_steady_hand", "Steady Hand",
          "Never misses, never surprises. The Weir issues it to anyone who has been startled once too often.",
          "White", "Utility", "DamageProc", 1.00, 0.022, 10, "Starting shop"),
    magic("magic_gamblers_sigil", "Gambler's Sigil",
          "Old Guard camp-work, technically forbidden. Enforcement was reportedly inconsistent.",
          "Green", "Utility", "DamageProc", 0.012, 3.30, 35, "Quest drops, chapter 3+"),
    magic("magic_metronome", "Wardens' Metronome",
          "It keeps the count whether or not anybody is listening, which is the Watch in one object.",
          "Blue", "Utility", "DamageProc", 1.00, 0.070, 80, "Quest drops, chapter 4+"),
    magic("magic_last_lamp_wick", "Wick of the Last Lamp",
          "It burns very slowly and it has never gone out. Arveth's lamp is still lit on the same principle.",
          "Purple", "Utility", "DamageProc", 1.00, 0.110, 165, "Black Archive"),
    magic("magic_one_true_swing", "The One True Swing",
          "Gravewend's peasant got exactly one. The pitchfork remembers the shape of it.",
          "Orange", "Utility", "DamageProc", 0.004, 42.00, 0, "Hollow Marches, vanishingly rare"),

    # ══ THE JOKE, KEPT DRY ════════════════════════════════════════════════════════════════════
    magic("magic_considered_inaction", "Considered Inaction",
          "The archdevil's contribution to the war effort. He thought about it for an Age and then, "
          "on balance, contributed this. It is genuinely quite good, which nobody has forgiven.",
          "Orange", "Utility", "GoldProc", 0.09, 4.10, 0, "The Stoned Devil"),
]


def main():
    p = CONTENT / "magics.json"
    data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    have = {m["id"] for m in data}
    added = [m for m in NEW if m["id"] not in have]
    data.extend(added)
    io.open(p, "w", encoding="utf-8", newline="\n").write(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    cats = collections.Counter(m["category"] for m in data)
    rars = collections.Counter(m["rarity"] for m in data)
    print("magics: +%d (total %d)" % (len(added), len(data)))
    print("  by category: %s" % dict(sorted(cats.items())))
    print("  by rarity:   %s" % dict(sorted(rars.items())))
    print("  expected value (chance x amount), new rows only:")
    for m in added:
        ev = m["procChance"] * m["procAmount"] if m["effectType"] != "CritChanceFlat" else m["procAmount"]
        print("    %-34s %-7s %-9s EV %.3f" % (m["id"], m["rarity"], m["category"], ev))


if __name__ == "__main__":
    main()
