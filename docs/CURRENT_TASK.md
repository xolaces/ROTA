# ROTA — Current Task

*Updated 2026-05-30. Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

## Just completed
- **v0.2.0** Beta Access Control · **v0.2.1** hardening · **v0.2.2** class regen + raid size set +
  raid on-hit XP/gold · **v0.2.3** discernment crit (raids) · **v0.2.4** character gear.
  All merged, tagged, pushed to origin.
- Current: 219 unit + 7 integration = 226 tests green, 0 warnings. `main` @ v0.2.4 synced with origin.

## v0.2.4 summary (just shipped)
8-slot equipment system. `player_equipment` table, unique per slot per player. JSON-driven `gear.json`.
Raid damage uses effective stats (base + gear ATK/DEF). Mount proc: once per hit, roll chance% →
add procPercent × base damage (before crit). GET/PUT/DELETE `/api/equipment/{slot}`.
Starter set: 8 Grey pieces, full-set +1 ATK/+3 DEF, Draft Horse mount (5% proc ×200%).

## NEXT (in order)
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
