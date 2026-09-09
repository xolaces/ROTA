#!/usr/bin/env python3
"""Builds assets/icons/ART_BRIEF.md — the style lock plus one prompt per content entry.

Split out from gen_placeholder_icons.py so there is exactly one source for prompts. The icon
generator makes placeholder PNGs; this makes the brief for the real art.

THE ONE RULE THAT MATTERS. The engine draws the rarity frame and tints the tile background itself,
using the client's own rarity colours. So the ART must carry NO frame, NO border and NO rarity
colour — otherwise every asset is locked to one rarity forever and a re-tier means a redraw. That
also settles the owner's open question about colour: it does not belong in the prompt at all.
"""
import collections
import io
import re
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
CONTENT = ROOT / "src" / "ROTA.Api" / "content"
OUT = ROOT / "assets" / "icons" / "ART_BRIEF.md"

STYLE = """\
Flat 2D game icon, clean illustrated vector style. Bold readable silhouette, two or three tone
cel shading, one soft light source from the upper left. Thick soft outline. A single object,
centred, filling most of the frame. Fully transparent background. Square.

No pixel art. No photorealism. No painterly texture. No fine filigree or micro-detail. No text,
no numbers, no letters. No border, no frame, no card, no background scene, no ground shadow.
Simple and confident rather than intricate.\
"""

NEGATIVE = ("pixel art, 8-bit, photorealistic, hyperdetailed, intricate, ornate filigree, "
            "painterly brushwork, text, watermark, border, frame, card layout, background scene, "
            "drop shadow on the ground, multiple objects, collage")


def load(name):
    p = CONTENT / name
    return json.load(io.open(p, encoding="utf-8")) if p.exists() else []


SLOT_NOUN = {
    "Head": "helmet or headgear", "Neck": "amulet or pendant", "Torso": "chest armour",
    "Gloves": "gauntlet or glove", "Boots": "boots", "Ring1": "ring", "Ring2": "ring",
    "Mount": "mount, shown as the animal alone in profile",
}
TYPE_NOUN = {
    "Material": "crafting material", "Sigil": "summoning sigil or seal",
    "Consumable": "flask, vial or potion", "StatBag": "pouch or cache", "Equipment": "equipment",
}


def rows():
    out = []
    for g in load("gear.json"):
        out.append(("gear", g.get("setId") or "gear (no set)", g["id"], g["name"],
                    SLOT_NOUN.get(g.get("slot"), "piece of equipment"), g.get("description", "")))
    # Sigils are raid x difficulty — 26 raids, four tiers each. The four share one seal and differ
    # only by tier, which the engine's rarity frame already conveys, so they need ONE artwork between
    # them. Collapsing them cuts 78 icons off the job for no visible loss.
    seen_sigils = set()
    for i in load("items.json"):
        if i.get("type") == "Sigil":
            base = re.sub(r"_(normal|hard|legendary|nightmare)$", "", i["id"])
            if base in seen_sigils:
                continue
            seen_sigils.add(base)
            out.append(("item", "items — Sigil (one per raid, shared by all four tiers)",
                        base, re.sub(r"\s*\((Normal|Hard|Legendary|Nightmare)\)\s*$", "", i["name"]),
                        TYPE_NOUN["Sigil"], i.get("description", "")))
            continue
        out.append(("item", "items — " + str(i.get("type", "")), i["id"], i["name"],
                    TYPE_NOUN.get(i.get("type"), "item"), i.get("description", "")))
    for m in load("magics.json"):
        out.append(("magic", "magics", m["id"], m["name"],
                    "arcane rune, sigil or talisman representing a spell effect",
                    m.get("description", "")))
    for u in load("units.json"):
        out.append(("unit", "units", u["id"], u["name"],
                    "character portrait bust of a %s %s" % (u.get("race", ""), u.get("role", "")),
                    u.get("description", "")))
    for l in load("legions.json"):
        out.append(("legion", "legions", l["id"], l["name"],
                    "military banner or standard", l.get("description", "")))
    for r in load("recipes.json"):
        out.append(("recipe", "crafting recipes", r["id"], r["name"],
                    "crafting or forging emblem for the item it produces", r.get("description", "")))
    for fn in ("raids.json", "guild_raids.json", "gauntlet_raids.json"):
        for r in load(fn):
            out.append(("raid", "raid bosses", r["id"], r["name"],
                        "monster or boss portrait", r.get("description", "")))
    return out


def clean(text, limit=190):
    t = " ".join((text or "").split())
    if len(t) <= limit:
        return t
    cut = t[:limit]
    return cut[:cut.rfind(" ")] + "…"


def main():
    data = rows()
    groups = collections.OrderedDict()
    for fam, group, gid, name, noun, desc in data:
        groups.setdefault((fam, group), []).append((gid, name, noun, desc))

    with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        w = fh.write
        w("# ROTA — art brief\n\n")
        w("**%d icons, in %d batches.** Generated from the shipped content, so it cannot drift "
          "from what the game actually contains.\n\n" % (len(data), len(groups)))

        w("## How to use this\n\n")
        w("Work one batch at a time. Paste the **style block** first, then the batch's items. "
          "Generating a whole set in one sitting is what makes the set look like a set.\n\n")

        w("## The style block — paste this before every batch\n\n")
        w("```\n%s\n```\n\n" % STYLE)
        w("**Negative prompt:**\n\n```\n%s\n```\n\n" % NEGATIVE)

        w("## Output specification\n\n")
        w("| | |\n|---|---|\n")
        w("| Format | PNG with real alpha transparency |\n")
        w("| Size | 512 x 512 square, downscaled in-engine |\n")
        w("| Background | fully transparent — not white, not a colour |\n")
        w("| Filename | the `id` exactly, e.g. `gear_conscript_helm.png` |\n")
        w("| Goes in | `assets/icons/<family>/` replacing the placeholder of the same name |\n\n")

        w("## Three rules that are not stylistic\n\n")
        w("1. **No colour instruction, and no rarity colour in the art.** The engine draws the "
          "rarity frame and tints the tile behind the icon using the client's own palette. Art that "
          "bakes in a rarity colour is locked to that tier forever, and re-tiering an item would "
          "mean redrawing it.\n")
        w("2. **No frame, border or card.** Same reason — the frame is drawn in-engine.\n")
        w("3. **Transparent background, every time.** A white background becomes a white box on the "
          "dark inventory tile.\n\n")
        w("**Sigils are collapsed.** The 104 sigil items are 26 raids x 4 difficulty tiers; the "
          "four tiers share one seal and are told apart by the frame the engine draws, so this "
          "brief asks for one artwork per raid. Name the file for the base id "
          "(`sigil_ironcolossus.png`) and all four tiers resolve to it.\n\n")
        w("Consistency beats quality here. A set of forty merely-good icons that share a style "
          "reads as a game; forty beautiful icons that do not share one reads as a folder.\n\n")
        w("---\n\n")

        for (fam, group), entries in groups.items():
            w("## %s — %d icons\n\n" % (group, len(entries)))
            w("*Family `%s` → `assets/icons/%s/`*\n\n" % (fam, fam))
            for gid, name, noun, desc in sorted(entries):
                w("**`%s`** — %s\n" % (gid, name))
                w("> %s. A %s. %s\n\n" % (name, noun, clean(desc)))
            w("---\n\n")

    print("wrote %s" % OUT.relative_to(ROOT))
    print("  %d icons across %d batches" % (len(data), len(groups)))
    for (fam, group), e in groups.items():
        print("    %-28s %3d" % (group, len(e)))


if __name__ == "__main__":
    main()
