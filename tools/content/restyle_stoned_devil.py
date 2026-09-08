#!/usr/bin/env python3
"""The Stoned Devil set: the joint helmet, the 42s, and a theme that never says its own name.

WHY NOT 420/420. Power is (ATK x 4) + DEF, so a 420/420 set is 2,100 against Sovereign's Regalia at
759 — 2.77x. It would not be the best Orange set, it would be the ONLY Orange set, and the other two
would become content nobody collects. That is precisely the Dawniversary pattern the research paper
blames for killing Dawn's new-player acquisition: each new set obsoleting the previous year's.

So 42 stays, twice, where it is funniest and cheapest: the joint and the goat carry 42/42 each. The
remaining six pieces sit at 14/14 — a third of 42, so every number in the set is on the same joke.
Set total 168/168, power 840, which is 1.11x Sovereign: a real edge for the rarest thing in the game
without deleting the other two chase sets.

THE THEME, which must never announce itself. The set gestures at a masked, nocturnal, worshipful
band-shaped thing WITHOUT naming it, quoting it, or nodding hard enough to be a reference: a
congregation, a vessel that is never refilled, sundown, kneeling, a garden. The archdevil himself
stays entirely sincere — he is not in on it — and the world around him stays straight-faced. That is
the whole mechanism: it is a joke the world tells ABOUT him, never a joke the world tells about
itself.

The drug humour is a USER's humour, not a pharmacology: he means to go out, he will look at it again,
he has said that for four hundred years, one is probably in the garden. Nothing is described in
detail because the funny part is the pace, not the substance.

DAWN'S DRUNKEN ANGEL is the thing being answered. Opposite vice, opposite altitude, opposite tempo,
same affection.
"""
import collections
import io
import json
import pathlib

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"

# id -> (name, slot-appropriate atk, def, description)
PIECES = {
    # ── THE JOINT. Owner's call, and it is the right slot: the crown of a being who has ────────
    #    declined every other crown offered to him.
    "gear_stoned_horns": (
        "The Sundown Ember", 42, 42,
        "Rolled at dusk, lit at dusk, and worn behind one ear like a circlet by an archdevil who "
        "maintains this is what it is for. The congregation stopped correcting him some centuries "
        "ago. It has never gone out, and nobody has ever seen him relight it."),

    "gear_stoned_censer": (
        "The Perpetual Censer", 14, 14,
        "A vessel lit some time during the Age of Dawn and never once refilled. There is still some "
        "left. There has always been some left. Those who come to kneel do not always remember, "
        "afterwards, what they came to ask."),

    "gear_stoned_robe": (
        "Robe of the Long Sabbatical", 14, 14,
        "Cut for a being who intended to sit down and has now been sitting down for an Age. "
        "Remarkably comfortable. Alarmingly hard to damage. It smells, faintly and permanently, "
        "of the room."),

    "gear_stoned_signet": (
        "Signet of Declined Ascendancy", 14, 14,
        "He was offered a throne. He read the terms, asked two questions nobody could answer, and "
        "went back inside. He says he will look at it again. He has been saying so for four "
        "hundred years."),

    "gear_stoned_band": (
        "Band of the Fourth Reconsideration", 14, 14,
        "There were three earlier ones. He is not looking for them. He is fairly certain one of "
        "them is in the garden."),

    # ── The second 42. It was 45/45; the joke is worth three points. ───────────────────────────
    "gear_stoned_goat": (
        "Extremely Relaxed Hell-Goat", 42, 42,
        "It will carry you anywhere at exactly one speed. Attempts to hurry it have never once "
        "succeeded and, by every account, have never once been forgiven."),

    "gear_stoned_slippers": (
        "Slippers of the Unwalked Path", 14, 14,
        "Immaculate. Not a scuff on them. He means to go out. He means to go out most evenings."),

    "gear_stoned_mitts": (
        "Mitts of Amiable Menace", 14, 14,
        "He shakes hands. It is worse than the alternative and takes considerably longer. He will "
        "ask after your family, and he will remember the answer, which is the genuinely "
        "frightening part."),
}

# Orange band: rarity buys chance, magnitude fixed at +110% — the same rule the magic catalogue
# now follows. Both procs are jokes about tempo, which is the set's whole comic register.
PROCS = {
    "gear_stoned_horns": (0.16, 1.10),   # it burns down mid-swing and he takes the moment
    "gear_stoned_goat":  (0.15, 1.10),   # the goat arrives when the goat arrives
}


def main():
    p = CONTENT / "gear.json"
    data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    by_id = {g["id"]: g for g in data}

    missing = [k for k in PIECES if k not in by_id]
    if missing:
        raise SystemExit("unknown gear ids: %s" % missing)

    shipped = {g["id"] for g in data if g.get("setId") == "set_stoned_devil"}
    unstyled = shipped - set(PIECES)
    if unstyled:
        raise SystemExit("set members not covered by this restyle: %s" % sorted(unstyled))

    for gid, (name, atk, dfn, desc) in PIECES.items():
        g = by_id[gid]
        g["name"] = name
        g["bonusAttack"] = atk
        g["bonusDefense"] = dfn
        g["description"] = desc
        g["procChance"] = None
        g["procPercent"] = None
    for gid, (chance, pct) in PROCS.items():
        by_id[gid]["procChance"] = chance
        by_id[gid]["procPercent"] = pct

    io.open(p, "w", encoding="utf-8", newline="\n").write(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    members = [g for g in data if g.get("setId") == "set_stoned_devil"]
    atk = sum(g["bonusAttack"] for g in members)
    dfn = sum(g["bonusDefense"] for g in members)
    print("The Stoned Devil — %d pieces" % len(members))
    for g in sorted(members, key=lambda x: x["slot"]):
        pr = ("  proc %.0f%% x +%.0f%%" % (g["procChance"] * 100, g["procPercent"] * 100)
              if g.get("procChance") else "")
        print("  %-8s %-36s %3d/%-3d%s" % (g["slot"], g["name"], g["bonusAttack"], g["bonusDefense"], pr))
    print("\n  total %d/%d   power (ATKx4+DEF) = %d" % (atk, dfn, atk * 4 + dfn))
    for other in ("set_sovereign", "set_pano"):
        o = [g for g in data if g.get("setId") == other]
        oa, od = sum(g["bonusAttack"] for g in o), sum(g["bonusDefense"] for g in o)
        print("  vs %-16s power %4d   ->  %.2fx" % (other, oa * 4 + od, (atk * 4 + dfn) / (oa * 4 + od)))


if __name__ == "__main__":
    main()
