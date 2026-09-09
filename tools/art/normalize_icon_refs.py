#!/usr/bin/env python3
"""Gives every content entry a resolvable icon reference, and reports what has no art behind it.

TWO CONVENTIONS EXIST AND BOTH ARE CORRECT. Items address art through `artKey`; everything else uses
`iconPath`. That is not an inconsistency to flatten — `artKey` is what lets the 104 sigils share 29
pictures, because a sigil's art depends on which raid it summons and not on which of the four
difficulty tiers it is. Collapsing the path into the id would force 104 drawings of the same seal.

So the rule is:

    items          icons/item/<artKey>.png       (many ids -> one file, by design)
    everything else  icons/<family>/<id>.png

This fills blanks and never overwrites a hand-set path.
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
    for fname, family in FAMILY.items():
        p = CONTENT / fname
        data = json.load(io.open(p, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
        changed = False
        for e in data:
            if not e.get("iconPath"):
                e["iconPath"] = "icons/%s/%s.png" % (family, e["id"])
                filled[fname] += 1
                changed = True
        if changed:
            io.open(p, "w", encoding="utf-8", newline="\n").write(
                json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    print("BLANK ICON REFERENCES FILLED")
    for f, n in sorted(filled.items()):
        print("   %-16s %d" % (f, n))
    if not filled:
        print("   none — every entry already had one")

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
