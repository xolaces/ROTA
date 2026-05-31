# ROTA — Current Task

*Updated 2026-05-31 (System 14 magic epic queued). Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

## Just completed
- **v0.2.0** Beta Access Control · **v0.2.1** hardening · **v0.2.2** class regen + raid size set +
  raid on-hit XP/gold · **v0.2.3** discernment crit (raids) · **v0.2.4** character gear ·
  **v0.2.5** conditional/stacking bonuses.
  All merged, tagged, pushed to origin.
- Current: 232 unit + 7 integration = 239 tests green, 0 warnings. `main` @ v0.2.5 synced with origin.

## v0.2.5 summary (just shipped)
Reusable conditional/stacking bonus framework. `ConditionalBonus` model (JSON-only, no C# changes to add bonuses). `ConditionalBonusEvaluator` static evaluator shared by gear and future troops/legions. 5 bonus types: `FlatAttack`, `FlatDefense`, `ProcChanceFlat` (clamp ≤1.0), `ProcAmountFlat`, `FlatDamagePercent` (applied after crit). 3 condition types: `OwnedUnitCount`, `OwnedTypeCount`, `EquippedTypeCount`. `EffectiveCombatData` gains `FlatDamagePercent` field. `GearDefinition` gains `ConditionalBonuses`; `ItemDefinition` gains `Tags`.

Debt fold-ins: reward atomicity (stamina spend inside advisory-lock tx — atomic with hit, no refund path), `ProcBonus` type fixed `double→long`, Function Reference refreshed.

## PENDING precursor: v0.2.5.1 hardening (small — do first)
Close v0.2.5 audit loose ends before the magic epic: (1) integration test proving the raid-hit stamina
spend rolls back on a lost race (no phantom loss); (2) document the proc-without-mount behavior in
`docs/specs/system-13-stacking-bonuses.md` + an XML comment; (3) confirm a unit test covers ProcChanceFlat
clamping at 1.0. Branch `v0.2.5.1-hardening`, test+docs only, tag v0.2.5.1.

## NEXT (large epic): System 14 — Raid Magic
Full spec + sliced task queue: **`docs/specs/system-14-raid-magic.md`**. Build the 6 slices IN ORDER —
one branch + build/test green + merge per slice, committed independently (never bundled). Auditor reviews
after a batch ("code for a good while before audit").
- Magic = a **shared raid-scoped PROC**; reuses the v0.2.5 proc pipeline / `ConditionalBonusEvaluator`
  (NOT a new combat system).
- Slots scale with size (Personal..Titanic → 1/2/3/4/5, config-driven). **World raids = Admin-only magic**
  (anti-grief). Ownership is permanent (acquisition is the economy sink); applying is free, slot-limited.
- Slices: (1) content/definitions → (2) ownership → (3) application+slots+world-gate **[DEEP]** →
  (4) damage proc effects **[DEEP/combat]** → (5) utility effects crit/gold/xp → (6) economy/acquisition.
- All locked decisions, data model, damage-formula order, and per-slice acceptance criteria are in the spec.

## Then (in order)
- **Legion system** (the bigger Dawn system): legions hold N Generals + N Troops; one Commander slot
  (equipment PROCS apply — not stats/sets); legion power ≈ `(100% + bonuses) × (Generals + Troops)`. Reuses
  the v0.2.5 conditional-bonus evaluator. Big, multi-batch.
- **v0.3.0 — C# API client SDK** (HTTP + DTOs) = the Unity client's layer. Unity Editor not runnable here;
  agent writes the SDK + MonoBehaviour scripts, owner wires scenes.

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
