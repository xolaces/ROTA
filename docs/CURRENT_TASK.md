# ROTA — Current Task

*Updated 2026-06-02 (System 15 Legion epic COMPLETE + audited). Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

## Just completed
- **v0.2.0–v0.2.5.1** (beta access → stacking bonuses + hardening) · **v0.2.6** System 14 Raid Magic
  (6 slices) · **v0.2.6.1** magic money-bug fix · **v0.2.7 System 15 Legion epic — COMPLETE (6 slices)**.
  All merged, tagged, pushed to origin.
- Current: **321 unit + 9 integration = 330 tests green, 0 warnings.** `main` past **v0.2.7-s6** + 3 post-fixes (gem-buy recovery · regen DTO · Gauntlet draft spec), synced with origin.

## v0.2.7 summary (System 15 — Legion, shipped + auditor-verified)
Units + legions with Race/Role/Attribute slot-typing. Legion power = a SEPARATE additive damage term folded
into `HitRaidAsync` preProc (crit-multiplied, counts toward contribution); `LegionConfig.PowerScaling` is the
master dominance dial; unit-ability procs reuse the proc pipeline with their own cap. Commander slot = a
procs-only gear slot (stat bonuses structurally excluded — lives in `player_commander_gear`, never reaches
`GetEffectiveCombatDataAsync`). Economy: idempotent `GrantUnit/GrantLegion`, gem shop (`/api/units/buy`,
`/api/legions/buy` — ownership pre-check + idempotent referenceId), unit/legion loot drops in raid+quest.
Deferred per spec (do NOT build): Armaments (Relic/Support/Siege), unit leveling, gorgets, Gauntlet
trophies/vs-raid-type bonuses, multi-copy troop stacking, Auto-Assign.

## Recently cleared (2026-06-02 — parallel subagent batch, auditor-merged)
- **Gem-buy lost-purchase — FIXED.** Tri-state `GemSpendOutcome` (Charged/AlreadyProcessed/InsufficientBalance);
  all 3 shops (magic/unit/legion) re-grant on AlreadyProcessed; real `BuyUnitIdempotencyTests` integration
  test proves single-charge + recovery vs the live ledger. Atomic spend+grant tx remains PHASE-2 (documented).
- **Regen DTO — DONE.** `ResourceValueResponse` now carries `RegenMinutesPerPoint` (double, class-based) +
  `SecondsToNextPoint`; legacy `RegenPerMinute` kept for back-compat. **Unblocks the Unity header timer** at go-live.

## Then (in order)
- **Gauntlet epic** (competitive leaderboard event; tightly coupled to Legion). **Draft spec written:
  `docs/specs/system-16-gauntlet.md` — BLOCKED on 28 owner OPEN QUESTIONS** (league boundaries, event
  cadence, score metric, Strikes-vs-Stamina, Wrath/Blessing permanent-vs-consumable, trophy stacking,
  token ledger…). Locked: 3 leagues, top-500, Gauntlet Tokens; **Wrath of the Ancients** (rank 1, 24%→500%)
  + **Blessing of the Ancients** (ranks 2–10, 13%→850%); Trophies (rank 1/10/500) +25/10/5% to all legion
  power. Combat: trophies multiply `rawLegionPower` before PowerScaling; Wrath/Blessing are capped
  `DamageProc` magics (the 850%-vs-magic-cap clamp is the Slice-4 landmine). Answer the Qs → I finalize → 6-slice build.
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
