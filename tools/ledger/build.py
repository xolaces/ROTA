"""The Loot Ledger: one page that says where every set piece and reforge part drops, with the
ladder odds, a grind simulation and a GPT prompt for DotD-style loot pages — all read from the
shipped content, so it cannot drift from the game.

    python tools/ledger/build.py      -> out/ledger/loot_ledger.html, out/ledger/loot_pages_prompt.md

Re-run after tools/content/reforge_sets.py or any loot-table change; publish the HTML as an
Artifact or open it locally."""
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).parent
ROOT = HERE.resolve().parents[1]
OUT_DIR = ROOT / "out" / "ledger"
import runpy  # noqa: E402
runpy.run_path(str(HERE / "build_data.py"))
sys.path.insert(0, str(ROOT / "tools" / "art"))
import gen_art_brief as brief  # noqa: E402  STYLE / NEGATIVE / SET_MOTIF

data = json.loads((OUT_DIR / "loot_data.json").read_text(encoding="utf-8"))
sets = {s["id"]: s for s in data["sets"]}


def set_lines():
    out = []
    for s in data["sets"]:
        pieces = ", ".join("%s (%s, %d/%d → %d/%d)" % (p["name"], p["slot"], p["atk"], p["def"], p["ratk"], p["rdef"]) for p in s["pieces"])
        motif = brief.SET_MOTIF.get(s["id"], "")
        out.append("- %s set [%s] — parts: %s, %s. Pieces: %s. Look: %s" % (s["name"], s["rarity"], s["scrap"]["name"], s["tack"]["name"], pieces, motif))
    return "\n".join(out)


def raid_lines():
    out = []
    for r in data["raids"]:
        n = r["difficulties"]["Nightmare"]
        rungs = ", ".join("{:,}".format(x["threshold"]) for x in n["rungs"])
        mount = [g for x in n["rungs"] for g in x["gear"]]
        parts = "/".join(sets[s]["name"] for s in r["sets"])
        hp = "timer-only, %sh" % r["timerHours"] if r["baseHp"] == 0 else "{:,} HP on Normal".format(r["baseHp"])
        out.append("- %s (%s; %s; %s; tags %s) — parts: %s; damage rungs: %s%s" % (
            r["name"], r["id"], r["tier"] + ("/" + r["grade"] if r["grade"] else ""), hp, ", ".join(r["tags"]) or "none",
            parts, rungs, ("; guaranteed on the last rung: " + ", ".join(m.replace("gear_", "").replace("_", " ") for m in mount)) if mount else ""))
    return "\n".join(out)


c = data["constants"]
PROMPT = f"""You are writing the raid loot pages for RISE OF THE ANCIENTS (ROTA), a browser game in the lineage of Dawn of the Dragons (DotD). I will give you the real content — sets, raids, drop rates — and the art rules every icon so far was drawn under. Produce one loot page per raid in the shape DotD's raid pages had, and an image prompt per item in our style. Do not invent items, sets, raids or rates; everything you need is below. Where you need flavour, write it in the register of the set descriptions.

WHAT A PAGE IS (DotD shape, one per raid):
1. A banner line: raid name, tier/grade, health (or timer), tags, and the one-sentence lore hook.
2. A "Damage ladder" table: one row per rung — damage dealt, stat points banked, reagents, and the chance of at least one Scrap and one Tack for a player who has passed that rung, per difficulty (Normal / Hard / Legendary / Nightmare). Rungs are cumulative: a player banks every rung they pass. The guaranteed mount sits on the last rung.
3. A "Parts" panel: which sets' Scrap and Tack this raid drops, what each reforges, and how many are needed per piece (scraps: Green 3, Blue 4, Purple 5, Orange 6; tack: 3 for the mount). Top-of-ladder odds of at least one: Scrap {int(c['scrapTop']['Normal']*100)}/{int(c['scrapTop']['Hard']*100)}/{int(c['scrapTop']['Legendary']*100)}/{int(c['scrapTop']['Nightmare']*100)}% and Tack {int(c['tackTop']['Normal']*100)}/{int(c['tackTop']['Hard']*100)}/{int(c['tackTop']['Legendary']*100)}/{int(c['tackTop']['Nightmare']*100)}% by difficulty; per rung the chance is q = 1 - (1 - top)^(1/rungs), and a player at rung k has 1 - (1 - q)^k.
4. A "Reforge" strip: base piece → reforged piece, attack/defence before → after (×{c['reforgeMult']}), gold (Green {c['gold']['Green']:,}, Blue {c['gold']['Blue']:,}, Purple {c['gold']['Purple']:,}, Orange {c['gold']['Orange']:,}; mounts ×{c['mountGoldMult']}). A worn piece is reforged in place.
5. Image prompts (see ART RULES) for: the raid banner (landscape, the boss as a flat-vector portrait bust), each Scrap and Tack, and — if the raid drops a guaranteed mount — that mount.

QUEST SIDE, for cross-references only: set pieces drop from quest zones at one rate per pool, every piece equal — 1 in 50 per click in chapters 1–2, 1 in 60 in chapters 3–4, 1 in 80 in chapters 5–6, 1 in 100 in chapter 7; ×1.15 / ×1.3 / ×1.5 on Hard / Legendary / Nightmare and ×2 on a zone's boss node. Pano's Vanguard set drops only from the Old Guard Ruins / Vanguard Approach quest line at 0.5% per piece.

THE SETS (name [rarity] — parts; pieces with base → reforged attack/defence; the set's visual signature):
{set_lines()}

THE RAIDS (name (id; tier; health; tags) — parts they drop; the damage rungs on the ladder):
{raid_lines()}

ART RULES — every icon in the game was drawn with this block; reuse it verbatim as the style block of each image prompt, then add the object description. The engine draws the rarity frame, so the art must carry no frame, no border and no rarity colour, on a transparent background.

STYLE BLOCK:
{brief.STYLE.format(budget="Three materials, about six shapes. One decorative element beyond pure function.")}

NEGATIVE PROMPT:
{brief.NEGATIVE}

For banners only, relax "one object" to "one figure, bust, centred" and use a 3:1 landscape frame with the same flat-vector rules. Scrap and Tack icons are objects: a Scrap is a small heap of the set's material (use the set's visual signature); a Tack is the mount's harness pieces — bit, buckle, strap — in the same material.

OUTPUT: Markdown. One H2 per raid, in the order given. Tables for the ladder. Image prompts in fenced code blocks, one per image, each self-contained (style block + object + negative prompt). Keep flavour text to one or two sentences per element and in the voice of the set descriptions: dry, concrete, never jokey — except the Stoned Devil, which may wink once. Start with the first chapter-1 raid and continue through all {len(data['raids'])}."""

html = (HERE / "loot_ledger.template.html").read_text(encoding="utf-8")
html = html.replace("/*DATA*/", json.dumps(data, ensure_ascii=False)).replace("/*PROMPT*/", json.dumps(PROMPT, ensure_ascii=False))
out = OUT_DIR / "loot_ledger.html"
out.write_text(html, encoding="utf-8")
(OUT_DIR / "loot_pages_prompt.md").write_text(PROMPT, encoding="utf-8")
print("wrote", out, out.stat().st_size // 1024, "KB; prompt", len(PROMPT), "chars")
