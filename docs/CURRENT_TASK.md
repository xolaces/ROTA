# ROTA — Current Task

*Updated 2026-06-02 (System 15 Legion epic COMPLETE + audited). Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

## Just completed
- **v0.2.0–v0.2.5.1** (beta access → stacking bonuses + hardening) · **v0.2.6** System 14 Raid Magic
  (6 slices) · **v0.2.6.1** magic money-bug fix · **v0.2.7 System 15 Legion epic — COMPLETE (6 slices)**.
  All merged, tagged, pushed to origin.
- Current: **315 unit + 8 integration = 323 tests green, 0 warnings.** `main` @ **v0.2.7-s6**, synced with origin.

## v0.2.7 summary (System 15 — Legion, shipped + auditor-verified)
Units + legions with Race/Role/Attribute slot-typing. Legion power = a SEPARATE additive damage term folded
into `HitRaidAsync` preProc (crit-multiplied, counts toward contribution); `LegionConfig.PowerScaling` is the
master dominance dial; unit-ability procs reuse the proc pipeline with their own cap. Commander slot = a
procs-only gear slot (stat bonuses structurally excluded — lives in `player_commander_gear`, never reaches
`GetEffectiveCombatDataAsync`). Economy: idempotent `GrantUnit/GrantLegion`, gem shop (`/api/units/buy`,
`/api/legions/buy` — ownership pre-check + idempotent referenceId), unit/legion loot drops in raid+quest.
Deferred per spec (do NOT build): Armaments (Relic/Support/Siege), unit leveling, gorgets, Gauntlet
trophies/vs-raid-type bonuses, multi-copy troop stacking, Auto-Assign.

## Open backend items (small — auditor-owned; clear before the next epic or as warm-ups)
- **Gem-buy partial-failure hardening** (PROJECT_STATE debt): `GemService.SpendGemsAsync` returns `false`
  for BOTH "insufficient" and "already-charged" → a charged-but-not-granted retry loses the purchase
  (no double-charge, but the item never arrives). Affects all 3 shops (magic/unit/legion). Fix = tri-state
  spend result + wrap spend+grant in one tx + a real buy-twice *integration* test. Spec as a focused task.
- **RegenPerMinute DTO** (Unity go-live blocker): `GET /api/players/me` returns the vestigial stored value,
  so the Unity header refill timer breaks against the live server. Return the class-based effective rate
  (or add a `SecondsToNextPoint` field).

## Then (in order)
- **Gauntlet epic** (competitive leaderboard event; tightly coupled to Legion). 3 level-leagues, top-500
  prizes, currency = Gauntlet Tokens → shop. LOCKED content from DotD: **SMITE → "Wrath of the Ancients"**
  (rank 1: 24%→500% dmg) and **Blessing of Mathala → "Blessing of the Ancients"** (ranks 2–10: 13%→850%),
  near-identical mechanics, acquired by Gauntlet rank; Gauntlet Trophies (rank 1/10/500) passively boost
  ALL legion power +25/10/5%. Spec when we reach it.
- **v0.3.0 — C# API client SDK** (HTTP + DTOs) = the Unity client's layer. Owner wires Unity scenes.

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
