# ROTA — Current Task

*Updated 2026-06-02 (System 15 Legion epic COMPLETE + audited). Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

## Just completed
- **v0.2.0–v0.2.5.1** (beta access → stacking bonuses + hardening) · **v0.2.6** System 14 Raid Magic
  (6 slices) · **v0.2.6.1** magic money-bug fix · **v0.2.7 System 15 Legion epic — COMPLETE (6 slices)**.
  All merged, tagged, pushed to origin.
- Current: **400 unit + 34 integration = 434 tests green, 0 warnings.** `main` at **v0.2.8-lb-s5**
  (**System 17 Leaderboards COMPLETE — all 5 slices** merged + auditor-verified), pushed/origin-synced.
  Pending-apply migration batch includes `AddLeaderboardEntry` (+ legion, commander).

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
- **Gauntlet epic** (competitive leaderboard event; tightly coupled to Legion). **Spec DECISION-COMPLETE +
  READY TO BUILD: `docs/specs/system-16-gauntlet.md`** — all 28 owner questions answered (canonical locked
  block at the top of the spec). Key calls: leagues by convergence tier (≤Ascendant / Luminary–Archon /
  Ancient+, floor L20, locked at entry); fixed-duration **admin-run** events, **auto-settle (idempotent)**;
  score = **cumulative damage**, ~60s Postgres snapshot, view top-200 / prizes top-500, per-event;
  **Strikes = persistent earned-first *ledger*** (no regen, carry over forever, uncapped gem buy, cost 1/5/20);
  **escalating-ladder personal** raids (`gauntlet_event_id` scoped); **Wrath/Blessing = per-event consumable
  OFF-CAP auras**, RETUNED to <100% base effective (Wrath 0.27×2.50=67.5%, Blessing 0.15×4.25=63.75%; owner ×1.25,
  former-owner ×1.10 honor-echo via `PlayerMagicHonor`; off-cap → NO magic-cap interaction);
  **trophies permanent, highest-only +25% cap**, multiply `rawLegionPower` before PowerScaling on ALL raids;
  **two currencies** (Gauntlet Tokens + Pitchfork) in one separate `gauntlet_currency_transactions` ledger;
  **power-focused** token shop. **Spec body FULLY INTEGRATED (Agent A, 2026-06-02)** — entities (StrikeTransaction,
  GauntletCurrencyTransaction, GauntletEvent/Entry, PlayerEventMagic, PlayerMagicHonor, PlayerGauntletTrophy),
  6 detailed slices (content → state/ledgers+lifecycle → leaderboard → combat [DEEP] → settlement → shop). **READY TO BUILD.**
- **System 17 — Global Leaderboards** (owner-introduced): `docs/specs/system-17-leaderboards.md` —
  6 boards (3 per-stat live snapshots ATK/DEF/Disc · questing energy wk+mo · raiders damage wk+mo · max-hit daily).
  **DECISION-COMPLETE (all Qs locked 2026-06-03)** — STATUS block canonical (retention=keep-forever ·
  global-only · L20 floor + exclude Admin (mods appear) · earliest-to-reach ties · on-read ranking · page 200 +
  caller rank; #1-reward CONTENT deferred to a later slice). **COMPLETE — all 5 slices DONE + auditor-merged + pushed**
  (tags `v0.2.8-lb-s1..s5`): S1 config/enums/`IPeriodKeyResolver` (ISO-week keys); S2 `LeaderboardEntry`
  aggregate table + race-safe `ON CONFLICT` repo (20-way concurrency test) + migration `AddLeaderboardEntry`
  (PENDING APPLY); S3 read service + `LeaderboardController` (`GET /api/leaderboards[/{board}]`) with
  eligibility enforced in SQL; S4 write hooks — `RecordEnergySpendAsync` (EnergyService, best-effort swallow) +
  `RecordRaidHitAsync` (RaidService, INSIDE the advisory-lock tx next to `RecordHit`, atomic, NOT on the Redis
  cached-replay path → no double-count); S5 Stat-board snapshot — `SnapshotStatBoardAsync` overwrites `Period=Live`
  rows for ATK/DEF/Disc from raw `PlayerStats.BaseAttack/BaseDefense/DiscernmentInvestment` (repo `SetValueAsync`,
  idempotent, `last_progress_at` preserved when unchanged) + `POST /api/admin/leaderboards/stat/refresh`
  `[AdminOnly]` + CLI `leaderboard-refresh-stat`. **Only deferred: #1-reward CONTENT** (additive, non-blocking).
- **Unity client — systems UI scaffolded (Agent B, 2026-06-02)**, `C:\Dev\ROTA.Client6`: 8 screens
  (Profile/Stats/Items/Equipment/Magic/Legion/Raid + Leaderboards-mock), full IRotaApi/Mock/Http, 0 compile errors.
  Pending: go-live (`useMock=false`, consume `RegenMinutesPerPoint`) · DTO-fidelity/JWT spot-check · commit the new `.cs.meta` files. Owner drives Play-mode.
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
