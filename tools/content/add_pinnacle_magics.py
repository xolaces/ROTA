#!/usr/bin/env python3
"""Pinnacle magics: the missing 2,500 placeholder, and the level-1,000 showcase.

WHAT PINNACLE MAGICS ARE. The first player to reach a milestone level designs that milestone's magic
outright — proc, item drop, or something stranger. Everyone who reaches the same level afterwards
receives that design and unlocks it permanently. You may own one of each; the per-raid magic slot cap
is unchanged, so owning them all does not mean casting them all.

THE EXCLUSIVITY (owner 2026-09-07). Exotic effects — auras, forced gear procs, drop modification —
exist ONLY on these magics. No ordinary magic and no item may carry one until the owner says
otherwise. PinnacleEffectExclusivityTests enforces it, so the rule cannot erode by a content pass
quietly copying a pinnacle entry as a template.

THREE GAPS THIS ALSO CLOSES, all found while adding the 2,500 entry:

  1. Levels 1,000 and 2,500 were pinnacle levels (LevelingConfig.PinnacleGemRewards) with no magic,
     while 15,000 and 25,000 had magics but were NOT pinnacle levels at all — IsPinnacleLevel reads
     that same map, so neither the gem reward nor the first-claim log fired at those two levels.
  2. All five shipped acquisition strings carried a literal U+FFFD replacement character where an
     em-dash belonged. Real mojibake in shipped content, not a console artefact.
  3. Nothing anywhere GRANTS a pinnacle magic to a player. That gap is code, not content, and is
     recorded in docs/AGENT_BACKLOG.md rather than papered over here.

THE LEVEL-1,000 DESIGN, and why this one. The owner asked for a showcase that demonstrates what a
pinnacle magic can do that an ordinary one cannot. A flat Attack aura was chosen over the other
candidates because of one property: being FLAT and RAID-WIDE, it helps the weakest participant most.
A 500-Attack newcomer gains about a quarter of their damage from it; the level-1,000 veteran who
applied it gains about two percent. Every other power in the game widens the gap between a veteran
and a newcomer. This is the only one that narrows it — which is the exact failure the research paper
blames for ending Dawn of the Dragons' new-player acquisition.

It also cannot inflate: it enters through Attack, so it flows through (ATK x 4 + DEF) x hitSize like
any stat point and never multiplies with a proc.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

PLACEHOLDER_DESC = (
    "PLACEHOLDER. A pinnacle magic awarded at level {lvl}. Its entire effect — proc, drop, or "
    "something stranger — is designed by the first player to reach this level; everyone who arrives "
    "afterwards receives that design and keeps it. Inert until then."
)

NEW = [
    # ── The missing placeholder. 2,500 was already a pinnacle level with nothing behind it. ────
    {
        "id": "magic_pinnacle_2500",
        "name": "Luminary's Vow",
        "description": PLACEHOLDER_DESC.format(lvl="2,500"),
        "rarity": "Orange",
        "category": "Damage",
        "effectType": "DamageProc",
        "procChance": 0.0,
        "procAmount": 0.0,
        "conditions": [],
        "stacks": False,
        "iconPath": "",
        "acquisition": "Pinnacle reward — level 2500 (design pending first claimant)",
        "gemPrice": 0,
    },
    # ── The showcase. Designed rather than left inert, because a stub that looks finished is ───
    #    exactly what the code-labelling rules forbid.
    {
        "id": "magic_pinnacle_1000",
        "name": "Ascendant's Banner",
        "description": (
            "The first standard planted past the thousandth mark. It does not make its bearer "
            "stronger so much as it makes everyone within sight of it harder to discourage — and the "
            "less a soldier brought to the field, the more the banner gives back. Adds a flat +120 "
            "Attack to every blow struck against this raid, by anyone, for as long as it stands."
        ),
        "rarity": "Orange",
        "category": "Utility",
        "effectType": "FlatAttackAura",
        "procChance": 1.0,        # always on; there is no roll
        "procAmount": 120.0,      # FLAT Attack, not a multiplier
        "conditions": [],
        "stacks": False,
        "iconPath": "icons/magic/pinnacle_1000.png",
        "acquisition": "Pinnacle reward — level 1000 (designed in-house as the showcase)",
        "gemPrice": 0,
    },
]


def main():
    p = CONTENT / "magics.json"
    data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    have = {m["id"] for m in data}

    # Repair the mojibake and restate the rule now that everyone-after-inherits is decided.
    fixed = 0
    for m in data:
        if not m["id"].startswith("magic_pinnacle"):
            continue
        lvl = m["id"].rsplit("_", 1)[1]
        acq = "Pinnacle reward — level %s (design pending first claimant)" % lvl
        if m.get("acquisition") != acq:
            m["acquisition"] = acq
            fixed += 1
        if "PLACEHOLDER" in (m.get("description") or ""):
            m["description"] = PLACEHOLDER_DESC.format(lvl=f"{int(lvl):,}")

    added = [m for m in NEW if m["id"] not in have]
    data.extend(added)

    io.open(p, "w", encoding="utf-8", newline="\n").write(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    pin = sorted((m for m in data if m["id"].startswith("magic_pinnacle")),
                 key=lambda m: int(m["id"].rsplit("_", 1)[1]))
    print("pinnacle magics: +%d (total %d), %d acquisition strings repaired\n" % (
        len(added), len(pin), fixed))
    for m in pin:
        state = "DESIGNED" if m["procAmount"] or m["effectType"] != "DamageProc" else "inert"
        print("  %-22s %-22s %-15s %-8s %s" % (
            m["id"], m["name"], m["effectType"], state,
            ("+%.0f flat Attack" % m["procAmount"]) if m["effectType"] == "FlatAttackAura" else ""))
    bad = [m["id"] for m in data if "�" in json.dumps(m, ensure_ascii=False)]
    print("\nreplacement characters remaining in magics.json: %s" % (bad or "none"))


if __name__ == "__main__":
    main()
