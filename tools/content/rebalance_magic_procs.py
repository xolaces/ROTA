#!/usr/bin/env python3
"""Bring every damage proc into a sane band, and make rarity buy CHANCE instead of MAGNITUDE.

THE PROBLEM THIS FIXES. The catalogue had drifted into exactly the end-of-Dawn shape the research
paper documents: 24 damage procs spanning +2% to +4200%, with expected values 840x apart. The worst
offender, The One True Swing, was 0.4% for +4200% — an ordinary expected value (0.168, about the
same as a good gloves proc) delivered as a slot machine that does nothing on 249 hits out of 250.

THE RULE. Magnitude is fixed per rarity inside the owner's 70-110% band; rarity moves only the
frequency. Two consequences, both deliberate:

  1. Expected value becomes MONOTONIC WITH RARITY BY CONSTRUCTION. The live bug where an Orange
     mount proc (EV 0.084) sat below the Grey starter's (0.100) cannot happen again in this family —
     you cannot write a higher-rarity entry that is worse without also writing a lower chance, and
     the bands do not overlap.
  2. Higher rarity FEELS different rather than merely bigger. An Orange proc fires about one hit in
     six, so a player watches it work; the old design paid the same expected damage as a jackpot
     nobody could feel.

    rarity          chance      magnitude      EV
    Grey / White    4-6%        +70%           .028 - .042
    Green           6-8%        +80%           .048 - .064
    Blue            8-11%       +90%           .072 - .099
    Purple          11-14%      +100%          .110 - .140
    Orange          14-18%      +110%          .154 - .198

HIT CHASERS. Owner wants one or two always-on magics; there were four. Whetstone (its description
already says "guaranteed small bonus on every hit") and Metronome (a metronome marks every beat)
keep 100% chance at a small magnitude. The other two join the band.

SMITE AND BLESSING OF MIGHT ARE THE PINNACLE (owner). Both were Blue mid-tier. They move to Orange
at the top of the band and take Orange pricing.

NOT TOUCHED. GoldProc and XpProc magics keep their large multipliers — a 600% XP proc is a pacing
knob, not a damage-scaling risk, and the owner's cap was explicitly about damage. CritChanceFlat
magics have no proc magnitude to cap. The five inert pinnacle placeholders stay at zero: they are
reserved for the milestone-level winners to design.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

# rarity -> fixed magnitude inside the owner's 70-110% band
MAGNITUDE = {"Grey": 0.70, "White": 0.70, "Green": 0.80, "Blue": 0.90, "Purple": 1.00, "Orange": 1.10}

# id -> (new rarity, new chance). Magnitude comes from the band above.
BAND = {
    # ── White: the floor. Cheap, frequent enough to notice, never decisive. ───────────────────
    "magic_steady_hand":            ("White", 0.05),
    "magic_lesser_poison":          ("White", 0.04),
    # ── Green ────────────────────────────────────────────────────────────────────────────────
    "magic_hollowpoint":            ("Green", 0.08),
    "magic_poison":                 ("Green", 0.07),
    "magic_gamblers_sigil":         ("Green", 0.06),
    # ── Blue ─────────────────────────────────────────────────────────────────────────────────
    "magic_rimefang":               ("Blue", 0.11),
    "magic_greater_poison":         ("Blue", 0.09),
    # ── Purple ───────────────────────────────────────────────────────────────────────────────
    "magic_wrathslag_ember":        ("Purple", 0.14),
    "magic_last_lamp_wick":         ("Purple", 0.13),
    "magic_sunder":                 ("Purple", 0.12),
    "magic_impending_doom":         ("Purple", 0.11),
    # ── Orange: Smite and Blessing of Might are the pinnacle pair (owner decision). ───────────
    "magic_smite":                  ("Orange", 0.18),
    "magic_blessing_of_might":      ("Orange", 0.17),
    "magic_kronarchs_last_order":   ("Orange", 0.15),
    # The big swing keeps its identity as the rarest Orange rather than as a lottery ticket.
    "magic_one_true_swing":         ("Orange", 0.14),
}

# Always-on: 100% chance, small magnitude. Exempt from the band by design.
HIT_CHASERS = {
    "magic_whetstone": ("White", 0.04),
    "magic_metronome": ("Blue", 0.09),
}

# Rarity moved, so the price should move with it.
REPRICE = {"magic_smite": 900, "magic_blessing_of_might": 1100}

# Descriptions that promised the old numbers.
REDESCRIBE = {
    "magic_smite": "The pinnacle strike. It lands often enough that you plan around it.",
    "magic_blessing_of_might": "The pinnacle boon. Sustained, reliable, and never a gamble.",
    "magic_one_true_swing": ("Gravewend's peasant got exactly one. The pitchfork remembers the shape "
                             "of it — and has learned to repeat it."),
    "magic_metronome": "It keeps time, and every beat lands. Small, certain, endless.",
    "magic_whetstone": "Sharpens every blow; a guaranteed small bonus on every hit.",
}

# Gauntlet RANK magics — seasonal trophies handed to the top player, with their own
# locked-number tests. Not catalogue items, and not mine to rebalance. See the note in
# docs/AGENT_BACKLOG.md: the off-cap path they were built for was removed, so they now
# flow through the ordinary magic loop at 3.4x the strongest normal Orange.
RANK_MAGICS = {"magic_wrath_of_the_ancients", "magic_blessing_of_the_ancients"}

# Inert by design — reserved for the milestone-level winners to design themselves.
RESERVED = {"magic_pinnacle_5000", "magic_pinnacle_7500", "magic_pinnacle_10000",
            "magic_pinnacle_15000", "magic_pinnacle_25000"}


def main():
    p = CONTENT / "magics.json"
    data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    by_id = {m["id"]: m for m in data}

    damage = [m for m in data if m["effectType"] == "DamageProc"]
    covered = set(BAND) | set(HIT_CHASERS) | RESERVED | RANK_MAGICS
    missing = [m["id"] for m in damage if m["id"] not in covered]
    if missing:
        raise SystemExit("unclassified damage procs (add them to BAND or HIT_CHASERS): %s" % missing)

    before = {m["id"]: (m["rarity"], m["procChance"], m["procAmount"]) for m in damage}

    for mid, (rarity, chance) in BAND.items():
        m = by_id[mid]
        m["rarity"] = rarity
        m["procChance"] = round(chance, 4)
        m["procAmount"] = MAGNITUDE[rarity]
    for mid, (rarity, amount) in HIT_CHASERS.items():
        m = by_id[mid]
        m["rarity"] = rarity
        m["procChance"] = 1.0
        m["procAmount"] = round(amount, 4)
    for mid, price in REPRICE.items():
        by_id[mid]["gemPrice"] = price
    for mid, text in REDESCRIBE.items():
        by_id[mid]["description"] = text

    io.open(p, "w", encoding="utf-8", newline="\n").write(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    order = {"Grey": 0, "White": 1, "Green": 2, "Blue": 3, "Purple": 4, "Orange": 5}
    rows = sorted((m for m in data if m["effectType"] == "DamageProc"),
                  key=lambda m: (order[m["rarity"]], -m["procChance"] * m["procAmount"]))
    print("%-32s %-22s %s" % ("id", "before", "after"))
    worst_ev = {}
    for m in rows:
        if m["id"] in RESERVED or m["id"] in RANK_MAGICS:
            continue
        b = before[m["id"]]
        ev = m["procChance"] * m["procAmount"]
        print("%-32s %-7s %5.1f%%x%-7.0f%%  ->  %-7s %5.1f%%x%-5.0f%%  EV %.3f" % (
            m["id"], b[0], b[1] * 100, b[2] * 100,
            m["rarity"], m["procChance"] * 100, m["procAmount"] * 100, ev))
        worst_ev.setdefault(m["rarity"], []).append(ev)

    print("\nEV band per rarity (must not overlap):")
    prev_hi = -1.0
    for r in ["White", "Green", "Blue", "Purple", "Orange"]:
        evs = worst_ev.get(r)
        if not evs:
            continue
        lo, hi = min(evs), max(evs)
        flag = "" if lo > prev_hi else "   <-- OVERLAP"
        print("   %-7s %.3f - %.3f%s" % (r, lo, hi, flag))
        prev_hi = hi
    print("\nmax damage magnitude now: %.0f%%  (was 4200%%)" %
          (100 * max(m["procAmount"] for m in data if m["effectType"] == "DamageProc")))


if __name__ == "__main__":
    main()
