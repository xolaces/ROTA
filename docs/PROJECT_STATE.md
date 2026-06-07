# ROTA — Project State (current truth)

*Verified 2026-05-30 by file inventory + source tracing + green `dotnet build`/`dotnet test` runs.*
*Single source of current truth. `CLAUDE.md` = session history; `changelog.md` = release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred; C# SDK is v0.3.0). Clean Architecture: `src/ROTA.{Api,Application,Domain,
Infrastructure,Shared}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## Build status (High — run this session)
- **400 unit + 34 integration = 434 tests pass. 0 warnings, 0 errors.**
- `main` past tag **v0.2.7-s6** (Legion epic complete) + 3 post-fixes merged & pushed (untagged hardening):
  gem-buy lost-purchase recovery, class-based regen DTO field, System 16 Gauntlet **draft** spec.
- Branches `slice/leaderboards-s1` + `slice/leaderboards-s2` merged to main (System 17 Slices 1+2).
- Branch `slice/leaderboards-s3` (System 17 Slice 3): ILeaderboardService + LeaderboardController + LeaderboardDTOs + eligibility-aware SQL (GetEligiblePageAsync/CountEligibleAsync/GetCallerRankAsync). +31 new tests (15 unit service, 7 unit controller, 9 integration eligibility).
- Branch `slice/leaderboards-s4` (System 17 Slice 4): RecordEnergySpendAsync + RecordRaidHitAsync write hooks on ILeaderboardService/LeaderboardService. EnergyService + RaidService wired. +12 new unit tests (period boundaries, idempotency guard, failure-swallow, Stamina exclusion, cached-replay guard).
- Branch `slice/leaderboards-s5` (System 17 Slice 5): Stat board snapshot. SetValueAsync + GetEligibleStatSnapshotAsync on ILeaderboardEntryRepository/LeaderboardEntryRepository. SnapshotStatBoardAsync on ILeaderboardService/LeaderboardService. POST /api/admin/leaderboards/stat/refresh [AdminOnly] + CLI leaderboard-refresh-stat. StatBoardRefreshResponse DTO. +9 unit tests (5 service snapshot, 4 admin controller) + 8 integration tests (ranking by ATK/DEF/Disc, eligibility exclusions, Moderator included, idempotency, value update, last_progress_at preservation + bump).

## Inventory (High)
11 controllers · 17 services · 19 entities · 25 enums · 18 repositories · 3 middleware ·
20 EF migrations (InitialCreate→AddLeaderboardEntry) · 8 content JSON files · GitHub Actions CI.
(Slice 3 adds: ILeaderboardService + LeaderboardService, LeaderboardController, LeaderboardDTOs, 3 new repo methods on ILeaderboardEntryRepository.)
(Slice 1 adds: LeaderboardBoard/LeaderboardPeriod/LeaderboardAggregation enums, LeaderboardConfig + supporting config enums, IPeriodKeyResolver + PeriodKeyResolver singleton.)
(Slice 6 adds: GemPrice to UnitDefinition/LegionDefinition, GemTransactionType.UnitPurchase/LegionPurchase,
UnitDropChance/LegionDropChance loot drop models, GrantUnitAsync/GrantLegionAsync/BuyUnitAsync/BuyLegionAsync
in ILegionService+LegionService. QuestService+RaidService wired to fire unit/legion drops from loot tables.
Two auditor fold-ins: AssignSlotAsync quantity > 0 guard; ComputeLegionPowerAsync reads LegionConfig coefficients.)

## Implemented & tested (High)
Auth · Rate limiting · Audit · Energy/resources · Player profile · Gem ledger · Quests+difficulty ·
Raid engine (pg advisory-lock, Redis idempotency) · Items/sigils · Stats · Class system ·
RBAC + beta keys + admin (REST+CLI) · Character gear (v0.2.4) · Conditional/stacking bonuses (v0.2.5) ·
Raid magic content layer (System 14 Slice 1 — MagicDefinition + IMagicDefinitionProvider + magics.json) ·
**Raid magic ownership (System 14 Slice 2 — PlayerMagic entity + GET /api/magics) ·
**Raid magic application (System 14 Slice 3 — RaidMagic + slot-cap advisory lock + world gate; DEEP review)** ·
**Raid magic damage procs (System 14 Slice 4 — DamageProc magic fires in HitRaidAsync; MagicProcBonus+MagicProcs in RaidHitResponse)** ·
**Raid magic utility effects (System 14 Slice 5 — CritChanceFlat/GoldProc/XpProc wired into HitRaidAsync; MagicCritBonus in RaidHitResponse)** ·
**Raid magic economy (System 14 Slice 6 — GrantMagicAsync idempotent upsert, BuyMagicAsync gem shop, magicDrops in loot tables wired into RaidService+QuestService)**.
- **Resource regen is class-based (v0.2.2):** energy/stamina/guild regen derive from `ClassConfig`
  (minutes-per-point). **GuildStamina now regenerates** (was 0). Stored `RegenPerMinute` is vestigial.
- **RaidSize set (v0.2.2):** Personal/Small/Medium/Large/Titanic, participant caps 1/10/25/50/250,
  enforced pre-spend on hit. Personal = summoner-only.
- **Raid on-hit rewards (v0.2.2):** XP = single 1–4 roll × stamina; gold = stamina × per-raid
  `goldPerStamina`; hit response now returns per-hit `XpGained`/`GoldGained`/`DamageDealt`.
- **Discernment crit (v0.2.3):** raid hits crit via `DiscernmentInvestment` — chance 5%→15% (+10%
  hard cap @1000 disc), damage 1.5×→2.5× (@5000 disc), tunable `CombatConfig`.
- **Character gear (v0.2.4):** 8 slots. Raid damage uses effective stats. Mount proc once per hit.
- **Conditional bonuses (v0.2.5):** JSON-only bonus framework. `ConditionalBonusEvaluator` shared
  by gear/future legions. 5 bonus types, 3 condition types. `FlatDamagePercent` applied after crit.
  Reward atomicity fixed: stamina spend inside advisory-lock tx (atomic rollback). `ProcBonus` is `long`.

## Content state (High)
Minimal playable slice: 2 chapters, 5 quest nodes (3 battle + 2 boss), 2 raids, 12 items, 2 loot
tables. Loop works; thin.

## Partially implemented (High)
- SignalR registered, **no hubs mapped** (real-time inert).
- Admin "panel" is API-only (no UI).

## System 15 — Legion (v0.2.7, Slices 1–6 — COMPLETE)
- **Slice 1**: 6 enums, UnitDefinition/LegionDefinition models, IUnitDefinitionProvider/ILegionDefinitionProvider, content/units.json (8 units) + content/legions.json (3 legions). Tag v0.2.7-s1.
- **Slice 2**: PlayerUnit/PlayerLegion entities, EF configs, migration AddLegionOwnership, repos, LegionService (GetOwnedUnitsAsync/GetOwnedLegionsAsync), LegionController (GET /api/units, /api/legions). Tag v0.2.7-s2.
- **Slice 3**: PlayerLegionSlot entity, EF config, migration AddLegionSlots, PlayerLegionSlotRepository, full LegionService (SetActiveLegionAsync, AssignSlotAsync, ClearSlotAsync, ComputeLegionPowerAsync, GetLegionDetailAsync). Slot constraint validation (Race/Role/Attribute). Tag v0.2.7-s3.
- **Slice 4 (DEEP)**: LegionConfig (PowerScaling, UnitCoefficients, MaxUnitProcBonus), legion power integrated into HitRaidAsync preProc (same RNG multiplier+hitSize as charBase; inline from injected repos, NOT LegionService.ComputeLegionPowerAsync), unit-ability proc phase (separate cap from magic), RaidHitResponse gains LegionPower/UnitProcBonus/UnitProcs. Tag v0.2.7-s4.
- **Slice 5 (MODERATE)**: Commander slot — PlayerCommanderGear entity (one row per player, upsert in place), IPlayerCommanderGearRepository, EF config + migration AddCommanderGear. ILegionService: EquipCommanderAsync/UnequipCommanderAsync/GetCommanderAsync. LegionController: PUT/DELETE/GET /api/legions/commander. Combat: commander gear proc fires in proc phase off preProc (stats deliberately excluded — PlayerCommanderGear never reaches GetEffectiveCombatDataAsync path). RaidHitResponse: CommanderProcFired/CommanderProcBonus. Tag v0.2.7-s5.
- **Slice 6 (MODERATE)**: Economy/acquisition — GemPrice on unit/legion defs. GrantUnitAsync/GrantLegionAsync (idempotent upsert). BuyUnitAsync/BuyLegionAsync: ownership pre-check → AlreadyOwned 409 without charge, idempotent referenceId (unitbuy:{playerId}:{id} / legionbuy:{…}). GemTransactionType.UnitPurchase/LegionPurchase. LootTable: UnitDropChance/LegionDropChance added to ThresholdReward (raid) and LootTableDifficulty (quest); wired in RaidService.DistributeKillRewardsAsync and QuestService.ApplyLootAsync. Auditor fold-ins: AssignSlotAsync Quantity>0 guard; ComputeLegionPowerAsync reads LegionConfig.UnitCoefficients (no hardcoded values). LegionService gains IGemService + IOptions<LegionConfig>; QuestService+RaidService gain ILegionService dep. POST /api/units/buy + POST /api/legions/buy. Tag v0.2.7-s6.

## System 17 — Global Leaderboards
- **Slice 1** (merged): `LeaderboardBoard` / `LeaderboardPeriod` / `LeaderboardAggregation` enums (Domain). `LeaderboardConfig` (IOptions, appsettings-bound, startup-validated — Timezone/MinLevel/PageSize/WeekStartsOn). `IPeriodKeyResolver` interface + `PeriodKeyResolver` singleton (ISOWeek-correct weekly keys, UTC-normalised, config validation at ctor). 24 unit tests covering all period-key boundaries + ISO year-edge cases + bad-config guards.
- **Slice 2** (merged): `LeaderboardEntry` entity (private setters, no EF attributes; `Create`/`AddValue`/`MaxValue`/`SetRank` methods). Fluent EF config: table `leaderboard_entry`, snake_case, enum columns stored as `int` with NO `HasDefaultValue` (no sentinel needed), unique index `ix_leaderboard_entry_upsert_key` on `(player_id, board, period_key)`, read index `ix_leaderboard_entry_board_period_value` on `(board, period_key, value)`, FK index `ix_leaderboard_entry_player_id`. Migration `AddLeaderboardEntry`. `DbSet<LeaderboardEntry>`. `ILeaderboardEntryRepository` + `LeaderboardEntryRepository` (raw Npgsql ON CONFLICT upsert — race-safe increment and max-update). 8 unit tests (domain methods) + 6 integration tests (Testcontainers Postgres: create+accumulate, 20-concurrent no-lost-updates, max-only-raises, page-order tiebreak, lookup present/absent, soft-delete filter).
- **Slice 3** (branch `slice/leaderboards-s3`): `ILeaderboardService` + `LeaderboardService`. `LeaderboardDTOs` (LeaderboardEntryDto/LeaderboardPageResponse/LeaderboardSummary — Board+Period as strings per ROTA.Shared convention). `LeaderboardController [Authorize]`: GET /api/leaderboards, GET /api/leaderboards/{board}. Eligibility-aware SQL: `ILeaderboardEntryRepository` extended with `GetEligiblePageAsync` / `CountEligibleAsync` / `GetCallerRankAsync` — all JOIN players and apply `is_deleted=false AND is_banned=false AND level>=@minLevel AND (roles & 4)=0 when excludeAdmins`. Caller rank computed in SQL as (COUNT eligible strictly above) + 1. 15 unit tests (service: page ordering, rank offset, caller-off-page, empty board, bad combos, period key format, display-name fallback) + 7 unit tests (controller: 200/400 map) + 9 integration tests (Testcontainers Postgres: banned excluded, soft-deleted excluded, below-MinLevel excluded, Admin excluded+Mod included, tiebreak, caller rank with ineligible interleaved, banned caller=null, count excludes ineligible, display-name hydration).
- **Slice 4** (branch `slice/leaderboards-s4`): Write hooks. `ILeaderboardService` extended with `RecordEnergySpendAsync(playerId, amount, at)` and `RecordRaidHitAsync(playerId, damageFinal, at)`. `LeaderboardService` implements both — fan-out to board+period calls via the existing `ILeaderboardEntryRepository`. `EnergyService.SpendEnergyAsync`: after the atomic spend + audit write, calls `RecordEnergySpendAsync` for `ResourceType.Energy` only (Stamina/GuildStamina excluded, Q6); failure is swallowed+logged — spend never rolls back. `RaidService.HitRaidAsync`: inside the `AtomicApplyHitAsync` advisory-lock callback immediately after `participantFinal.RecordHit(damageFinal)`, calls `RecordRaidHitAsync` — rides the ambient transaction; never reached on the Redis cached-replay early-return path. No new migrations. +12 unit tests (LeaderboardWriteHookTests: period boundaries, leaderboard failure swallow, Stamina exclusion, cached-replay guard, damageFinal pass-through).
- **Slice 5** (branch `slice/leaderboards-s5`): Stat board snapshot. `ILeaderboardEntryRepository` extended with `SetValueAsync(playerId, board, period, periodKey, value, at)` (overwrite-upsert: unconditional value overwrite; `last_progress_at` bumped ONLY when value changed — tiebreak survives repeated snapshots) and `GetEligibleStatSnapshotAsync(minLevel, excludeAdmins)` (single SQL join players+player_stats applying full eligibility predicate). `ILeaderboardService.SnapshotStatBoardAsync()` queries eligible stat snapshots and calls `SetValueAsync` × 3 per player (StatAttack=BaseAttack, StatDefense=BaseDefense, StatDiscernment=DiscernmentInvestment), Period=Live, period_key="live". `AdminController` extended: `POST /api/admin/leaderboards/stat/refresh` [AdminOnly] — DB actor re-verify, calls SnapshotStatBoardAsync, writes audit_log (`StatBoardRefreshed`), returns `StatBoardRefreshResponse{PlayersSnapshotted, SnapshotAt}`. CLI `leaderboard-refresh-stat` added to AdminCli. `StatBoardRefreshResponse` DTO added. No new migrations. +5 unit service tests + 4 unit controller tests + 8 integration tests (ranking, eligibility, Moderator included, idempotency, value update, last_progress_at preserved/bumped).

## System 16 — Gauntlet (in progress)
- **Slice 1** (branch `feat/system16-gauntlet-s1-content`): content + definitions only. 7 enums
  (GauntletLeague/EventState/RewardKind/TrophyTier/Currency/CurrencyTransactionType/StrikeTransactionType).
  `GauntletConfig` (IOptions, appsettings-bound, league bounds/entry floor/prize+page counts/strike rates).
  Content models GauntletPrizeTable+Band, GauntletTrophyDefinition, dedicated GauntletRaidDefinition (NOT
  reusing RaidDefinition). `MagicDefinition.OffCap` field (combat reads it in S4). `IGauntletContentProvider`
  + `GauntletContentProvider` (singleton, startup-validated, eager-constructed). content/gauntlet_prizes.json
  (7 bands → 1..500) + gauntlet_trophies.json (3) + gauntlet_raids.json (6-stage ladder); 2 off-cap magic
  rows (Wrath 0.27/2.50, Blessing 0.15/4.25). +25 unit tests. No entities/migrations/combat (Slices 2/4).
  Build 0 errors; **551 unit + 35 integration green**. (NOTE: global counts elsewhere in this doc predate
  Phase 2 / Systems 18–20 and are stale — separate reconciliation pending.)

- **Slice 2** (branch `feat/system16-gauntlet-s2-ledgers`): persistence + ledgers + lifecycle + join.
  7 entities (GauntletEvent guarded state machine; GauntletEntry league-locked + unique per event/player;
  StrikeTransaction + GauntletCurrencyTransaction append-only ledgers; PlayerGauntletTrophy, PlayerEventMagic,
  PlayerMagicHonor) + Fluent configs + ONE consolidated migration `AddGauntletSystem` (DB update NOT run —
  coordinate). 7 repos with tri-state SpendAsync on both ledgers (idempotency-first + unique-violation
  backstop; AlreadyCharged ≠ Insufficient). IGauntletService (join: league-lock via ResolveLeague + reject
  no-event/L<20/banned/deleted + idempotent; gem→strikes buy with client-key lost-purchase recovery).
  IGauntletAdminService (open ≤1-active / close / idempotent state-only settle — payout = S5). GauntletController
  [Authorize] + GauntletAdminController [AdminOnly + actor re-verify] + CLI gauntlet-open/close/settle.
  ActiveRaid.GauntletEventId additive column+FK. GemTransactionType.GauntletStrikePurchase; GauntletConfig.StrikeGemPrice.
  +38 unit + 10 integration. Build 0 errors; **589 unit + 45 integration green**.

- **Slice 3** (branch `feat/system16-gauntlet-s3-leaderboard`): scoring aggregation + per-league snapshot
  rank + leaderboard read (NO combat changes — S4 wires the score hook). `IGauntletScoringService` +
  `GauntletScoringService` (scoped): `UpdateScoreAsync` (atomic score += delta; tie_break_at advances only
  on a positive delta), `RecomputeRanksAsync` (idempotent), `GetLeaderboardAsync` → `GauntletLeaderboardResponse`.
  `IGauntletEntryRepository`/`GauntletEntryRepository` extended with raw-Npgsql ranked reads (mirrors
  LeaderboardEntryRepository): `IncrementScoreAsync` (single race-safe UPDATE, ambient-tx aware),
  `RecomputeRanksAsync` (one set-based UPDATE via `ROW_NUMBER() OVER (PARTITION BY league ORDER BY score DESC,
  tie_break_at ASC)` → last_rank), `GetLeaderboardPageAsync` (ORDER BY last_rank, league-isolated, joined to
  players for DisplayName, excludes un-snapshotted), `GetRankAndScoreAsync` (league-scoped caller standing),
  `CountRankedAsync`. `GauntletRankSnapshotService` hosted (singleton; DI scope per tick; PeriodicTimer at
  `ScoreSnapshotSeconds`; no-op when no active event; try/catch never crashes host). GET
  /api/gauntlet/leaderboard?league= [Authorize] (400 invalid/missing league; empty board when none active).
  DTOs GauntletLeaderboardResponse/GauntletLeaderboardEntryDto. No new migrations (GauntletEntry + ranking
  index already exist). +6 unit + 17 integration. Build 0 errors; **595 unit + 62 integration green**.

## Not implemented (High)
Game client (C# SDK = v0.3.0) · discernment quest-drop-quality (later) ·
moderation (back-burnered) · world chat · guild · gauntlet · gacha/pity ·
equipment crafting / consumables · gear set bonuses (Phase 2) ·
structured log sink / monitoring · background jobs.

## Known issues / debt (High)
- (Resolved v0.2.5: reward atomicity — stamina spend now inside advisory-lock tx.)
- (Resolved v0.2.5: ProcBonus type — now `long`.)
- **(Resolved 2026-06-02: gem-buy lost-purchase recovery.)** `GemService.SpendGemsAsync` now returns a
  tri-state `GemSpendOutcome` (Charged / AlreadyProcessed / InsufficientBalance). All 3 shops
  (magic/unit/legion) treat AlreadyProcessed as success and re-run the idempotent grant, so a
  charged-but-not-granted retry recovers the item with no double-charge. A real Testcontainers integration
  test (`BuyUnitIdempotencyTests`) proves single-charge + re-grant against the live ledger + unique index.
  **Still PHASE-2:** wrapping spend+grant in one DB transaction (needs a cross-repo transaction-scope
  abstraction) — the recovery path makes this a hardening step, not a correctness fix; `// PHASE-2` notes
  are in all 3 buy methods.
- (Documented Phase-2, pre-existing: `GemService` concurrent balance-overspend — non-atomic balance check
  + insert can drive balance negative under contention across *different* referenceIds; advisory lock deferred.)

## Balance values (accepted per CURRENT_TASK; tunable in appsettings)
- **Regen pacing — accepted:** Conscript = 5.0 min/point for BOTH energy and stamina (~2 h for full
  energy); higher classes regen faster (Eternal = 1.0). `ClassConfig.RegenMinutesPerPoint`. Offset by
  level-up RefillResource + future consumables. Revisit only if beta feedback shows it's too slow.
- GuildStamina regenerates at 2.0 min/point.

## To verify (below High)
Test coverage % · exact lockout thresholds · production deployment topology.

## Key docs
`docs/OPERATIONS.md` · `docs/ARCHITECTURE.md` · `docs/CURRENT_TASK.md` · `docs/DESIGN_NORTHSTAR.md`.
