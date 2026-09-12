"""
Puts the deep relics of chapters 1 and 2 into their zone bosses' tables.

wire_drop_tables.py carries DEEP_DROPS for (1, 0) and (2, 0), but those two bosses — q003 in the
Old Guard Ruins and q005 on the Vanguard Approach — kept their hand-authored lt_quest_* tables and
never received a generated lt_zone_*b table, so the Colossus-Core Shard and the Vanguard's Cold
Token were listed and never dropped anywhere (RG1). This writes exactly what the generator would
have: the boss multiplier (×2) on the DEEP_DROPS base chance, the per-difficulty CHANCE ramp,
rareScaling on so Discernment lifts it. Idempotent.

Owner, 2026-09-12: "the relics can drop from their deep zones".
"""
import collections
import io
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from wire_drop_tables import CHANCE, DEEP_DROPS, chance  # noqa: E402

CONTENT = pathlib.Path(__file__).resolve().parents[2] / "src" / "ROTA.Api" / "content"
BOSS_MULT = 2.0

# Deep zone → the hand-authored boss table that stands where a generated one would be.
HOMES = {(1, 0): "lt_quest_q003", (2, 0): "lt_quest_q005"}


def main():
    lp = CONTENT / "loot_tables.json"
    tables = json.load(io.open(lp, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    by_id = {t["id"]: t for t in tables}
    wired = []
    for zone, table_id in HOMES.items():
        table = by_id[table_id]
        for kind, gid, base, _why in DEEP_DROPS[zone]:
            if kind != "gear":
                continue
            for diff, entry in table["difficulties"].items():
                drops = entry.setdefault("gearDrops", [])
                row = next((d for d in drops if d["gearDefinitionId"] == gid), None)
                if row is None:
                    row = collections.OrderedDict()
                    drops.append(row)
                row["gearDefinitionId"] = gid
                row["quantity"] = 1
                row["chance"] = chance(base * BOSS_MULT, diff)
                row["rareScaling"] = True
            wired.append((gid, table_id, chance(base * BOSS_MULT, "Normal"), chance(base * BOSS_MULT, "Nightmare")))

    io.open(lp, "w", encoding="utf-8", newline="\n").write(
        json.dumps(tables, indent=2, ensure_ascii=False) + "\n")
    for gid, tid, lo, hi in wired:
        print("%-24s -> %-16s %.4f%% Normal .. %.4f%% Nightmare" % (gid, tid, lo * 100, hi * 100))


if __name__ == "__main__":
    main()
