# ROTA — Current Task

*Updated 2026-05-30 (v0.2.5 queued). Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

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

## NEXT: v0.2.5 — Conditional / stacking bonuses (decisions LOCKED with owner)
Reusable, content-driven bonus framework so future gear **and troops** declare *"for every X owned,
gain Y"* with **no C# changes** — JSON only. Owner examples: "own 10 farmhand troops → +10% damage,"
"+10 attack per owned unit" (ref: DotD anniversary troops). All bonus math is **server-side at combat
time** — never client-trusted.

**Locked JSON contract** (one entry per bonus, attached to a gear/troop definition):
```json
"conditionalBonuses": [
  { "conditionType": "OwnedUnitCount", "conditionTarget": "troop_farmhand",
    "perCount": 10, "bonusType": "ProcChanceFlat", "bonusAmount": 0.05 }
]
```
Eval rule: `floor(owned / perCount) × bonusAmount`, accumulated per `bonusType`.

- **bonusType set** (string in JSON → enum): `FlatAttack`, `FlatDefense`, `ProcChanceFlat`
  (clamp ≤1.0), `ProcAmountFlat`, `FlatDamagePercent` (applied last).
- **conditionType set:** `OwnedUnitCount` (specific item/unit ID), `OwnedTypeCount` (by `tag`),
  `EquippedSlot` (binary).
- **Pipeline seam:** `EquipmentService.GetEffectiveCombatDataAsync` — inject `IPlayerInventoryRepository`
  (exists) to read counts. `FlatAttack/Defense` fold into effective stats; `ProcChanceFlat/AmountFlat`
  modify `GearProcData` before the proc roll; `FlatDamagePercent` → new `EffectiveCombatData` field that
  `RaidService` applies to `damageFinal` after proc+crit. Build a **shared evaluator** (not gear-specific)
  so troops/legions reuse it verbatim.
- **Dependency:** troops/legions don't exist yet → prove the framework now against existing inventory
  items (sigils/materials); troop-targeting lights up when legions land. This is the whole point —
  contract + evaluator ready in advance.
- **Open decisions for spec author:** proc-chance clamp + any global damage-% ceiling; whether
  `EffectiveCombatData` carries flat new fields or a `ResolvedBonuses` sub-object; per-hit eval (default,
  sub-ms indexed read) vs cached.

**Fold into the same branch (`v0.2.5-stacking-bonuses`) — debt that's timely now:**
1. **Refresh `docs/ROTA_Function_Reference.md`** (stale: dated 2026-05-29, missing v0.2.4 equipment,
   class system still marked "to be added"). New sessions are told to read it first — drift misleads.
2. **Reward atomicity:** raid stamina spend runs in its own tx *outside* the advisory lock — crash
   between spend and apply loses stamina (`// Phase 2` in RaidService). We're reopening `HitRaidAsync`
   anyway; cheapest moment to wrap spend+apply in one scope. (Owner: confirm bundling.)
3. **`ProcBonus` type:** DTO `RaidHitResponse.ProcBonus` is `double` but computed `long` — align to `long`.

## Then (in order)
- **Legion system** (the bigger Dawn system): legions hold N Generals (by type) + N Troops; one
  Commander slot (on commander, only equipment PROCS apply — not stats/sets); legion power ≈
  `(100% + bonuses) × (Generals + Troops)`; generals carry their own legion gear. Reuses the v0.2.5
  conditional-bonus evaluator. Big, multi-batch.
- **v0.3.0 — C# API client SDK** (HTTP + DTOs) = the Unity client's layer. Unity Editor not runnable
  here; agent writes the SDK + MonoBehaviour scripts, owner wires scenes.

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
