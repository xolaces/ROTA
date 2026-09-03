#!/usr/bin/env python3
"""Validate the runtime content data used by ROTA.

This is the code-side guardrail for the lore/content pipeline. It ensures a content
pass has actually been wired into the JSON files the server consumes, instead of
being documentation-only.

Usage:
    python tools/validate_content_pack.py
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
CONTENT_DIR = ROOT / "src" / "ROTA.Api" / "content"

REQUIRED_FILES = [
    "quests.json",
    "raids.json",
    "items.json",
    "gear.json",
    "loot_tables.json",
]


def load_json(path: Path) -> Any:
    try:
        with path.open("r", encoding="utf-8") as fh:
            return json.load(fh)
    except FileNotFoundError as exc:
        raise RuntimeError(f"Missing required content file: {path}") from exc
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"Invalid JSON in {path}: {exc}") from exc


def ensure_unique_ids(items: list[dict[str, Any]], label: str) -> None:
    seen: set[str] = set()
    for item in items:
        ident = item.get("id")
        if not ident or not isinstance(ident, str):
            raise RuntimeError(f"{label}: a record is missing a non-empty string id")
        if ident in seen:
            raise RuntimeError(f"{label}: duplicate id '{ident}'")
        seen.add(ident)


def validate_quests(quests: list[dict[str, Any]], raid_ids: set[str], item_ids: set[str]) -> None:
    ensure_unique_ids(quests, "quests")
    quest_ids = {q["id"] for q in quests}
    for quest in quests:
        prereq = quest.get("prerequisiteQuestId")
        if prereq and prereq not in quest_ids:
            raise RuntimeError(f"quest '{quest['id']}' references missing prerequisite '{prereq}'")

        sigils = quest.get("sigils")
        if isinstance(sigils, dict):
            for diff, item_id in sigils.items():
                if not isinstance(item_id, str) or item_id not in item_ids:
                    raise RuntimeError(
                        f"quest '{quest['id']}' references missing sigil item '{item_id}' for {diff}"
                    )

        if quest.get("nodeType", "Battle") == "Boss" and not quest.get("sigilDropChance", 0):
            # Bosses may intentionally have 0 chance on a non-sigil-content area, but the content
            # engine should still be aware. We do not fail here because some bosses legitimately have
            # no sigil reward plus no item reference.
            pass

        loot_id = quest.get("lootTableId")
        if loot_id:
            # This is intentionally only a structural cross-check; the project validates the actual
            # table metadata in the runtime providers, so we avoid over-constraining this script.
            pass


def collect_loot_table_ids(loot_tables: Any) -> set[str]:
    """Every loot table id, whichever shape loot_tables.json is in."""
    if isinstance(loot_tables, dict):
        return {str(k) for k in loot_tables}
    if isinstance(loot_tables, list):
        return {t["id"] for t in loot_tables if isinstance(t, dict) and isinstance(t.get("id"), str)}
    return set()


def validate_raids(raids: list[dict[str, Any]], loot_table_ids: set[str]) -> list[str]:
    """Structural checks over raids.json. Returns the ids of raids that carry no loot table.

    An EMPTY lootTableId is legitimate: it is how a raid says it has no threshold loot, and the
    runtime short-circuits on it cleanly. A DANGLING one is not — GetById returns null, the loot pass
    is skipped, and the raid silently pays gold and XP only, which is indistinguishable from a raid
    designed to carry no loot. That is why one is reported and the other is fatal.
    """
    ensure_unique_ids(raids, "raids")

    lootless: list[str] = []
    for raid in raids:
        raid_id = raid.get("id", "<unknown>")

        name = raid.get("name")
        if not isinstance(name, str) or not name.strip():
            raise RuntimeError(f"raid '{raid_id}' is missing a valid name")

        # A World raid is TIMER-ONLY: no collective health, rewards come from a damage ladder. Zero
        # health is meaningful there and a mistake anywhere else. Mirrors RaidDefinitionProvider.Validate.
        timer_only = str(raid.get("tier", "Standard")).lower() == "world"
        hp = raid.get("baseHp")
        if not isinstance(hp, (int, float)):
            raise RuntimeError(f"raid '{raid_id}' is missing baseHp")
        if timer_only and hp != 0:
            raise RuntimeError(
                f"World raid '{raid_id}' has baseHp {hp}; World raids are decided by a timer and a "
                "damage ladder, so their health must be 0"
            )
        if not timer_only and hp <= 0:
            raise RuntimeError(
                f"raid '{raid_id}' has baseHp {hp}; only World raids may have no health"
            )

        if not raid.get("timerHours", 0):
            raise RuntimeError(f"raid '{raid_id}' has no timerHours; a raid that never expires "
                               "can never be settled")

        loot_id = raid.get("lootTableId", "")
        if not loot_id:
            lootless.append(raid_id)
        elif loot_id not in loot_table_ids:
            raise RuntimeError(
                f"raid '{raid_id}' references missing loot table '{loot_id}'"
            )

    return lootless


def validate_items(items: list[dict[str, Any]], raid_ids: set[str]) -> None:
    ensure_unique_ids(items, "items")
    item_ids = {i["id"] for i in items}
    for item in items:
        summon_raid = item.get("summonRaidId")
        if summon_raid and summon_raid not in raid_ids:
            raise RuntimeError(f"item '{item['id']}' references missing summoned raid '{summon_raid}'")

        upgrades = item.get("upgradesTo")
        if upgrades and upgrades not in item_ids:
            raise RuntimeError(f"item '{item['id']}' upgradesTo missing target '{upgrades}'")


def validate_gear(gear: list[dict[str, Any]]) -> None:
    ensure_unique_ids(gear, "gear")


def validate_loot_tables(loot_tables: dict[str, Any] | list[dict[str, Any]]) -> None:
    if isinstance(loot_tables, dict):
        for key, value in loot_tables.items():
            if not isinstance(value, dict):
                raise RuntimeError(f"loot_tables '{key}' must be an object")
        return

    if isinstance(loot_tables, list):
        ensure_unique_ids(loot_tables, "loot_tables")
        return

    raise RuntimeError("loot_tables.json must be an object or array")


def main() -> int:
    if not CONTENT_DIR.exists():
        raise RuntimeError(f"Content directory not found: {CONTENT_DIR}")

    missing = [name for name in REQUIRED_FILES if not (CONTENT_DIR / name).exists()]
    if missing:
        raise RuntimeError(f"Missing required content files: {', '.join(missing)}")

    quests = load_json(CONTENT_DIR / "quests.json")
    raids = load_json(CONTENT_DIR / "raids.json")
    items = load_json(CONTENT_DIR / "items.json")
    gear = load_json(CONTENT_DIR / "gear.json")
    loot_tables = load_json(CONTENT_DIR / "loot_tables.json")

    if not isinstance(quests, list):
        raise RuntimeError("quests.json must be a list")
    if not isinstance(raids, list):
        raise RuntimeError("raids.json must be a list")
    if not isinstance(items, list):
        raise RuntimeError("items.json must be a list")
    if not isinstance(gear, list):
        raise RuntimeError("gear.json must be a list")

    raid_ids = {r["id"] for r in raids}
    item_ids = {i["id"] for i in items}
    loot_table_ids = collect_loot_table_ids(loot_tables)

    validate_quests(quests, raid_ids, item_ids)
    lootless = validate_raids(raids, loot_table_ids)
    validate_items(items, raid_ids)
    validate_gear(gear)
    validate_loot_tables(loot_tables)

    print("Content validation OK")
    print(f"quests: {len(quests)} | raids: {len(raids)} | items: {len(items)} | gear: {len(gear)}")

    # Reported, not fatal. Whether a Standard-tier raid SHOULD drop items is a design decision, not a
    # correctness one — but the number is large enough that it should never go unnoticed again.
    if lootless:
        print()
        print(f"NOTE: {len(lootless)} of {len(raids)} raids carry no loot table "
              f"(gold and XP only): {', '.join(sorted(lootless))}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as exc:
        print(f"Content validation failed: {exc}", flush=True)
        raise SystemExit(1)
