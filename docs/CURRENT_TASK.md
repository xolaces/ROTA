# ROTA — Current Task

*Updated 2026-05-30 (v0.2.5 shipped). Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

## Just completed
- **v0.2.0** Beta Access Control · **v0.2.1** hardening · **v0.2.2** class regen + raid size set +
  raid on-hit XP/gold · **v0.2.3** discernment crit (raids) · **v0.2.4** character gear ·
  **v0.2.5** conditional/stacking bonuses.
  All merged, tagged, pushed to origin.
- Current: 232 unit + 7 integration = 239 tests green, 0 warnings. `main` @ v0.2.5 synced with origin.

## v0.2.5 summary (just shipped)
Reusable conditional/stacking bonus framework. `ConditionalBonus` model (JSON-only, no C# changes to add bonuses). `ConditionalBonusEvaluator` static evaluator shared by gear and future troops/legions. 5 bonus types: `FlatAttack`, `FlatDefense`, `ProcChanceFlat` (clamp ≤1.0), `ProcAmountFlat`, `FlatDamagePercent` (applied after crit). 3 condition types: `OwnedUnitCount`, `OwnedTypeCount`, `EquippedTypeCount`. `EffectiveCombatData` gains `FlatDamagePercent` field. `GearDefinition` gains `ConditionalBonuses`; `ItemDefinition` gains `Tags`.

Debt fold-ins: reward atomicity (stamina spend inside advisory-lock tx — atomic with hit, no refund path), `ProcBonus` type fixed `double→long`, Function Reference refreshed.

## NEXT: Legion system (the bigger Dawn system)
Legions hold N Generals (by type) + N Troops; one Commander slot (equipment PROCS apply — not stats/sets); legion power ≈ `(100% + bonuses) × (Generals + Troops)`; generals carry their own legion gear. Reuses the v0.2.5 conditional-bonus evaluator. Big, multi-batch.

## Then (in order)
- **v0.3.0 — C# API client SDK** (HTTP + DTOs) = the Unity client's layer. Unity Editor not runnable here; agent writes the SDK + MonoBehaviour scripts, owner wires scenes.

## Deferred / back-burnered
Discernment quest-drop-quality · moderation (no chat/mods/players) · per-raid xpPerStamina ·
gear set bonuses · N+1 in `DistributeKillRewardsAsync` (per-participant queries inside the lock on
kill — invisible at current raid sizes, batch only if raids get crowded) · content depth
(2 raids / 5 quests is thin — needs authoring alongside systems).

## Balance values owner has signed off / flagged (tunable in appsettings)
- Regen: class-based, Conscript 5 min/point energy+stamina (accepted — level-up RefillResource +
  future consumables offset it). · Crit: 5%→15% chance (+10% hard cap @1000 disc), 1.5×→2.5× dmg
  (@5000 disc). `CombatConfig` / `ClassConfig`.

## Hard rule (learned)
Build-green ≠ correct. After any agent build: run the app, trace behavior-touching changes (combat/
migrations/concurrency/money), smoke-test the CLI. Light-check only low-risk batches (docs/CI/additive).
