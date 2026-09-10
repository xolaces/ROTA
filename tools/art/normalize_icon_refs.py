#!/usr/bin/env python3
"""Gives every content entry a resolvable icon reference, and reports what has no art behind it.

TWO CONVENTIONS EXIST AND BOTH ARE CORRECT. Items address art through `artKey`; everything else uses
`iconPath`. That is not an inconsistency to flatten — `artKey` is what lets the 104 sigils share 29
pictures, because a sigil's art depends on which raid it summons and not on which of the four
difficulty tiers it is. Collapsing the path into the id would force 104 drawings of the same seal.

So the rule is:

    items          icons/item/<artKey>.png       (many ids -> one file, by design)
    everything else  icons/<family>/<id>.png

It used to fill blanks and leave hand-set paths alone, on the reasonable theory that a path someone
typed deliberately knows something the rule does not. It did not: 186 of the 362 references were
hand-set, and every one of them was wrong. They drop the id's family prefix — `gear_conscript_helm`
addressed as `icons/gear/conscript_helm.png` — and the recipes point at `icons/craft/`, a folder
that has never existed. Nothing caught it because nothing served the files, so nothing ever asked
for one.

So the rule now wins, and a wrong path is repaired rather than preserved. The one exception stays
the one that carries meaning: a path already pointing at a file that exists is left alone, which is
what protects an artKey-style share if one is ever set by hand.
"""
import collections
import io
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
CONTENT = ROOT / "src" / "ROTA.Api" / "content"
ICONS = ROOT / "assets" / "icons"

FAMILY = {
    "gear.json": "gear", "magics.json": "magic", "units.json": "unit",
    "legions.json": "legion", "recipes.json": "recipe",
}


def main():
    filled = collections.Counter()
    repaired = collections.Counter()
    examples = []
    for fname, family in FAMILY.items():
        p = CONTENT / fname
        data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
        changed = False
        for e in data:
            want = "icons/%s/%s.png" % (family, e["id"])
            have = e.get("iconPath") or ""
            if have == want:
                continue
            if have and (ICONS.parent / have).exists():
                continue                      # points at a real file — it knows something we do not
            e["iconPath"] = want
            (repaired if have else filled)[fname] += 1
            if have and len(examples) < 6:
                examples.append((e["id"], have, want))
            changed = True
        if changed:
            io.open(p, "w", encoding="utf-8", newline="\n").write(
                json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    print("BLANK ICON REFERENCES FILLED")
    for f, n in sorted(filled.items()):
        print("   %-16s %d" % (f, n))
    if not filled:
        print("   none — every entry already had one")

    print("\nBROKEN REFERENCES REPAIRED")
    for f, n in sorted(repaired.items()):
        print("   %-16s %d" % (f, n))
    if not repaired:
        print("   none — every path already resolved")
    for i, was, now in examples:
        print("      %-30s %s  ->  %s" % (i, was, now))

    # Which references have no PNG behind them yet?
    missing = collections.Counter()
    total = collections.Counter()
    for fname, family in FAMILY.items():
        for e in json.load(io.open(CONTENT / fname, encoding="utf-8")):
            total[family] += 1
            if not (ICONS / family / (e["id"] + ".png")).exists():
                missing[family] += 1
    items = json.load(io.open(CONTENT / "items.json", encoding="utf-8"))
    keys = {e.get("artKey") or e["id"] for e in items}
    total["item"] = len(keys)
    missing["item"] = sum(1 for k in keys if not (ICONS / "item" / (k + ".png")).exists())

    print("\nART COVERAGE (placeholder or real)")
    for fam in sorted(total):
        have = total[fam] - missing[fam]
        print("   %-8s %3d/%-3d  %s" % (fam, have, total[fam],
                                        "complete" if not missing[fam] else "%d missing" % missing[fam]))
    print("\n   items resolve through artKey: %d ids -> %d files" % (len(items), len(keys)))


if __name__ == "__main__":
    main()
