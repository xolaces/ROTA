# ROTA — Current Task

*Updated 2026-05-30. Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

## Just completed
- **v0.2.0** Beta Access Control · **v0.2.1** hardening · **v0.2.2** class regen + raid size set +
  raid on-hit XP/gold · **v0.2.3** discernment crit (raids). All merged, tagged, pushed to origin.
- Current: 215 tests green (208 unit + 7 integration), 0 warnings. `main` synced with origin.

## NEXT: v0.2.4 — Character gear (decisions LOCKED with the owner)
Build the 8-slot character equipment system. Locked design:
- **Slots (8):** head, neck, torso, ring1, ring2, mount, boots, gloves.
- **Effective stats:** equipped gear adds flat ATK/DEF on top of base stat investment. The raid
  damage formula `(ATK×4 + DEF) × hitSize` switches from `BaseAttack/BaseDefense` to **effective**
  (base + gear). Player profile shows effective stats.
- **Mount = a proc** in the raid-hit pipeline (alongside crit): **once per hit/attack**, roll a
  `chance%` → add `proc% × base damage`. Bounded, Dawn-style (e.g. ~5–40% chance, ~200–400% proc).
- **Set bonuses: DEFERRED** — first gear system is slots + flat ATK/DEF + mount proc only.
- **JSON-driven** like `items.json`: one gear entry = slot + stats + optional proc + an `iconPath`
  for future images. Adding gear = adding a JSON object (no code change). Add an `Equipment` item
  type / gear definition + a `player_equipment` table (player_id, slot, gear_definition_id, unique
  per slot). Equip/unequip endpoints.
- **Starter set:** ~1 ATK / 3 DEF, tutorial-tier (lowest rung of the Grey→Orange rarity ladder).
- Sequenced AFTER v0.2.3 (now merged) because both touch the raid damage pipeline.

## Then (in order)
- **Legion system** (the bigger Dawn system): legions hold N Generals (by type) + N Troops; one
  Commander slot (on commander, only equipment PROCS apply — not stats/sets); legion power ≈
  `(100% + bonuses) × (Generals + Troops)`; generals carry their own legion gear. Big, multi-batch.
- **v0.3.0 — C# API client SDK** (HTTP + DTOs) = the Unity client's layer. Unity Editor not runnable
  here; agent writes the SDK + MonoBehaviour scripts, owner wires scenes.

## Deferred / back-burnered
Reward-atomicity rework of the raid stamina spend · discernment quest-drop-quality · moderation
(no chat/mods/players) · per-raid xpPerStamina · gear set bonuses.

## Balance values owner has signed off / flagged (tunable in appsettings)
- Regen: class-based, Conscript 5 min/point energy+stamina (accepted — level-up RefillResource +
  future consumables offset it). · Crit: 5%→15% chance (+10% hard cap @1000 disc), 1.5×→2.5× dmg
  (@5000 disc). `CombatConfig` / `ClassConfig`.

## Hard rule (learned)
Build-green ≠ correct. After any agent build: run the app, trace behavior-touching changes (combat/
migrations/concurrency/money), smoke-test the CLI. Light-check only low-risk batches (docs/CI/additive).
