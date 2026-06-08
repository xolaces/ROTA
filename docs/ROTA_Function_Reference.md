# ROTA Function Reference
Last updated: 2026-06-08 (System 22 Masteries Phase A — Slice 7: Discernment drop-quality — PHASE A COMPLETE)
Update when adding public methods or entities.

---

## System 22 — Masteries Core (Phase A, Slice 7 — Discernment drop-quality / rarity-upgrade)

Spec: docs/specs/active/system-22-masteries-core.md. No migration (content-model field only).

- **`ItemDefinition.UpgradesTo` / `GearDefinition.UpgradesTo`** (`string?`) — next-tier-up id; null = never. Validated
  at startup by `ItemDefinitionProvider` / `GearDefinitionProvider`: resolves + strictly higher rarity (Orange = ceiling).
  ItemDefinitionProvider also added a duplicate-id guard.
- **Quest item quality-upgrade (wired):** `QuestService.ProcessQuestLootAsync(... discernmentQualityChance ...)` — a fired
  chance drop rolls the chance; on success `ResolveQualityUpgrade` substitutes the item's `UpgradesTo` (single step).
  Guaranteed drops never upgrade.
- **Starter ladder:** `mat_iron_shard`→`mat_arcane_dust`, `statbag_minor`→`statbag_major`.
- **Deferred:** gear-drop + raid-threshold quality-upgrade wiring (Orange-ceiling gear / per-participant kill-loop reads).

**Slice 7 scope:** Discernment drop-quality. +8 unit tests. **System 22 Phase A COMPLETE** — 4 Ancients, level 1→5
challenge checklists, global+pledge modifiers (Wrath/Bulwark/Hoard/Discernment via existing combat+loot hooks),
Formula-B Overall Mastery Rating + titles + leaderboard board, lossless re-spec economy. 786 unit + 88 integration green.

---

## System 22 — Masteries Core (Phase A, Slice 6 — loot: Hoard + Discernment sigil-find)

Spec: docs/specs/active/system-22-masteries-core.md. No migration.

### IMasteryService (Slice 6 add)
| Method | Description |
|--------|-------------|
| `GetModifiersAsync(playerId)` | → `MasteryModifiers(Combat, Loot)` — both modifier sets from ONE mastery-state load. The raid hit path uses this (one read/hit instead of two). |

### Loot hooks
- **Quest (`QuestService.AttemptQuestAsync`):** `GetLootModifiersAsync` fetched best-effort (try/catch → neutral).
  Hoard `goldReward × HoardGoldMultiplier`; Hoard drop-rate folded into `ProcessQuestLootAsync`'s `Scale` closure
  (× `hoardDropMultiplier`, inside the 0.95 cap); Discernment post-first-clear sigil chance
  `× DiscernmentSigilFindMultiplier` (clamp ≤ 1.0; guaranteed first drop never scaled).
- **Raid (`RaidService.HitRaidAsync`):** on-hit gold `× masteryMods.Loot.HoardGoldMultiplier` (one combined read).
- **Deferred:** raid threshold-drop (kill-loot) Hoard scaling (per-participant reads on the kill path) — follow-up.

**Slice 6 scope:** Hoard (drop/gold) + Discernment sigil-find. Discernment drop-quality (rarity-upgrade) is Slice 7.
+3 unit tests.

---

## System 22 — Masteries Core (Phase A, Slice 5 — combat: Wrath + Bulwark)

Spec: docs/specs/active/system-22-masteries-core.md. No migration. No new combat path.

`RaidService.HitRaidAsync` loads `IMasteryService.GetCombatModifiersAsync(playerId)` once per hit (mastery-less → `(0,0)`):
- **Wrath:** `WrathLegionPercent` added into `totalLegionBonus` before the `bonusFraction` divide (additive with
  PowerBonus + General.LegionBonus; single-touch, ahead of the trophy stage + PowerScaling). Active-legion-gated; never Gauntlet.
- **Bulwark:** at the post-crit FlatDamagePercent stage, `flatDamageFraction = combat.FlatDamagePercent +
  (lockedRaid.GuildId != null ? BulwarkGuildDamageFraction : 0)`. Guild raids only; hard-capped in MasteryService; stacks additively with gear flat.
- **`RaidHitResponse`** gains `long WrathLegionBonus` + `long BulwarkBonus` (marginal display amounts; 0 when N/A).

**Slice 5 scope:** Wrath + Bulwark combat. Loot (Hoard + Discernment) is Slices 6–7. +4 unit tests; 700+ existing
hit tests run with neutral mods (byte-for-byte regression proof).

---

## System 22 — Masteries Core (Phase A, Slice 4 — activity counters + tier-up leveling)

Spec: docs/specs/active/system-22-masteries-core.md. No new migration (uses S2 tables).

### IPlayerMasteryActivityRepository (Slice 4 adds)
| Method | Description |
|--------|-------------|
| `IncrementAsync(playerId, activityType, delta)` | Raw ON CONFLICT upsert-increment (clones LeaderboardEntryRepository); ambient-tx aware (rides the raid advisory-lock tx). |
| `HasEventAsync(playerId, activityType, referenceId)` | Idempotency-ledger existence check (pre-check before insert). |
| `RecordEventAsync(playerId, activityType, referenceId)` | Inserts the idempotency event row. |

### IMasteryService (Slice 4 adds)
| Method | Description |
|--------|-------------|
| `RecordActivityAsync(playerId, activityType, amount=1, referenceId=null)` | Counter increment; exactly-once when referenceId supplied (HasEvent pre-check → RecordEvent → Increment). Best-effort at chokepoints. |
| `EvaluateTierUpsAsync(playerId)` | Off-hot-path: advances `PlayerMastery.Level` (monotonic, audited `MasteryLevelUp`, multi-tier) where cumulative counters meet the per-tier checklist. Triggered on read + after quest completion. |

### Chokepoints (the 8 counters the shipped checklists use)
- `RaidService.HitRaidAsync` (enlisted in advisory-lock tx): `RaidHit`+`RaidDamageDealt` (after RecordHit),
  `GuildRaidContribution` (guild fork), `GoldEarned` (on-hit gold), `RaidKill` (isKill, idempotent `mastery:kill:{raid}:{player}`).
- `QuestService.AttemptQuestAsync` (best-effort): `GoldEarned`, `QuestNodeCleared` (just-cleared), `QuestBossCleared`
  (boss+just-cleared) + `EvaluateTierUpsAsync`.
- `GauntletAdminService.SettleEventAsync` (best-effort + idempotent `mastery:rank:{event}:{player}`): `GauntletRankEarned`.
- New `IMasteryService` ctor dep on RaidService / QuestService / GauntletAdminService.
- **Reserved-but-unwired counters:** `EnergySpent`, `StaminaSpent`, `LevelGained` (no shipped checklist uses them yet).

**Slice 4 scope:** activity counters + tier-up leveling. Combat/loot modifier *consumption* is Slices 5–7.
+11 unit, +3 integration.

---

## System 22 — Masteries Core (Phase A, Slice 3 — pledge + re-spec economy)

Spec: docs/specs/active/system-22-masteries-core.md. Migration `AddMasteryRespecLedger` (NOT applied).

### Enums
`GemTransactionType.MasteryRespec=13`. `MasteryRespecKind { Paid=0, FreeMonthly=1, NewAncientUnlock=2 }`.

### Entity (`src/ROTA.Domain/Entities/MasteryRespecTransaction.cs`)
Append-only `{ Id, PlayerId, Kind (MasteryRespecKind), FromAncient (MasteryAncient?), ToAncient (MasteryAncient),
GemCost (int), ReferenceId, CreatedAt }`. Table `mastery_respec_transactions`; FK index on player_id; unique
`(player_id, reference_id)` (the period-cap + idempotency backstop). RefIds: paid `respec:paid:{p}:{isoYear}-Www`,
free-monthly `respec:free:{p}:{yyyy-MM}`, unlock `respec:unlock:{p}:{ancient}`.

### Cap store + repo (scoped)
- `IMasteryRespecCapStore` (Application) + `MasteryRespecCapStore` (Infrastructure, IConnectionMultiplexer):
  `IsPaidWeeklyUsedAsync` / `MarkPaidWeeklyUsedAsync` — Redis key `respec:paid:week:{p}`, TTL to next Monday 00:00 UTC.
- `IMasteryRespecRepository` + `MasteryRespecRepository`: `ReferenceExistsAsync(playerId, referenceId)`,
  `CreateAsync` (returns false on unique-violation).

### IMasteryService (Slice 3 add)
| Method | Description |
|--------|-------------|
| `RespecAsync(playerId, toAncient)` | → `MasteryRespecResult`. LOSSLESS pledge change; resolves free-unlock → free-monthly → paid-weekly; only flips the pledge (levels untouched); audited. |

`MasteryService` ctor gains: `IMasteryRespecRepository`, `IGemService`, `IMasteryRespecCapStore`, `IAuditLogRepository`.
`GetMasteriesAsync` RespecStatus now reflects real availability (free-monthly + paid-weekly unused?).

### Endpoint
`POST /api/masteries/pledge` [Authorize] (`MasteryController`) — `PledgeRequest { Ancient }` →
`MasteryRespecResult`. Maps: AncientNotFound/PlayerNotFound 404, AlreadyPledged/WeeklyCapReached 409,
InsufficientGems 422, Success 200. Validator `PledgeRequestValidator` (`MasteryValidators.cs`).

### DTOs (added to MasteryDTOs.cs)
`PledgeRequest`, `MasteryRespecResult` (Ok/Fail factories), `MasteryRespecFailureCode {None, AncientNotFound,
AlreadyPledged, WeeklyCapReached, InsufficientGems, PlayerNotFound}`.

**Slice 3 scope:** pledge + re-spec economy. No activity counters/leveling (Slice 4), no combat/loot (Slices 5–7).
+10 unit, +1 integration (paid-twice-charges-once vs real gem ledger).

---

## System 22 — Masteries Core (Phase A, Slice 2 — state + read API + rating)

Spec: docs/specs/active/system-22-masteries-core.md. Migration `AddMasterySystem` (NOT applied).

### Entities (`src/ROTA.Domain/Entities/`)
- `PlayerMastery` { Id, PlayerId, Ancient (MasteryAncient), Level (1..5), +audit }. `Create`→L1, `LevelUp()`,
  `SetLevel(int)` (monotonic — throws on decrease). Unique `(player_id, ancient)`. `MaxLevel=5`.
- `PlayerMasteryActivity` { Id, PlayerId, ActivityType, Counter (long), +audit }. `Create`, `Add(long)`. Unique
  `(player_id, activity_type)` (non-partial — the ON CONFLICT target for the Slice 4 race-safe increment).
- `MasteryActivityEvent` — append-only idempotency ledger { Id, PlayerId, ActivityType, ReferenceId, CreatedAt }.
  Unique partial `(player_id, activity_type, reference_id) WHERE reference_id IS NOT NULL`.
- `Player` additive: `MasteryAncient? ActivePledgeAncient` + `SetPledge(MasteryAncient)` / `ClearPledge()`
  (MasteryService sole writer; mirrors GuildId/GuildRank denorm).

### Repositories (scoped)
- `IPlayerMasteryRepository`: `GetForPlayerAsync`, `FindAsync(playerId, ancient)`, `UpsertAsync`,
  `EnsureAllAsync(playerId)` (lazy-creates the four L1 rows; returns all four), `GetAllRatingsAsync()` (rating-board
  snapshot source). Record `PlayerMasteryRatingRow(PlayerId, IReadOnlyDictionary<MasteryAncient,int> Levels)`.
- `IPlayerMasteryActivityRepository`: `GetForPlayerAsync` (Increment/event added in Slice 4).
- Impl `PlayerMasteryRepository` + `PlayerMasteryActivityRepository` (`Infrastructure/.../Repositories/MasteryRepositories.cs`).

### IMasteryService (`src/ROTA.Application/Interfaces/IMasteryService.cs`) + `MasteryService`
| Method | Description |
|--------|-------------|
| `GetMasteriesAsync(playerId)` | → `MasteryOverviewResponse` — ensures L1 rows; per-Ancient level/global%/effective% (pledged ×PledgeMultiplier, Bulwark clamped) + next-tier X/Y progress; rating; titles; respec status. |
| `GetCombatModifiersAsync(playerId)` | → `MasteryCombatModifiers(WrathLegionPercent, BulwarkGuildDamageFraction)` — Slice 5 hooks. |
| `GetLootModifiersAsync(playerId)` | → `MasteryLootModifiers(HoardDropMultiplier, HoardGoldMultiplier, DiscernmentSigilFindMultiplier, DiscernmentQualityChance)` — Slice 6/7 hooks. |
| `SnapshotRatingBoardAsync()` | Live snapshot of MasteryRatingActive/Lifetime via `ILeaderboardEntryRepository.SetValueAsync`; returns count. |
| `int ComputeRating(levels)` | Pure Formula B (Σ + breadth thresholds {≥2:+3,≥3:+5,≥4:+8,≥5:+12} + 2×count(L5) [+ 2×min if config]); missing→L1. |

Ctor: `(IMasteryDefinitionProvider, IPlayerMasteryRepository, IPlayerMasteryActivityRepository, IPlayerRepository,
ILeaderboardEntryRepository, IOptions<MasteryConfig>)`. Titles derived (no entity): breadth (Touched Everything/
Well-Rounded/Paragon/Ascendant) + `Master of <Ancient>` per maxed Ancient. Active==Lifetime in Phase A (monotonic levels).

### Enums / boards
`LeaderboardBoard.MasteryRatingActive=6`, `MasteryRatingLifetime=7` (Live period; registered in `LeaderboardService.AllBoards`).

### Endpoints / CLI
- `GET /api/masteries` [Authorize] → `MasteryOverviewResponse` (`MasteryController`).
- `POST /api/admin/masteries/rating/refresh` [AdminOnly + DB actor re-verify, audited] → `MasteryRatingRefreshResponse`
  (`MasteryAdminController`). CLI `mastery-refresh-rating`.
- `PlayerProfileResponse` gains `ActivePledge` (string?) + `MasteryRatingActive` (int); hydrated in `PlayerService.GetProfileAsync`.

### DTOs (`src/ROTA.Shared/DTOs/MasteryDTOs.cs`)
MasteryOverviewResponse, MasteryAncientDto, MasteryTierProgressDto, MasteryChecklistItemDto, MasteryRatingDto,
MasteryTitlesDto, MasteryRespecStatusDto, MasteryRatingRefreshResponse.

**Slice 2 scope:** state + read API + rating; no pledge/respec (Slice 3), no activity counters/leveling (Slice 4),
no combat/loot wiring (Slices 5–7). +13 unit tests.

---

## System 22 — Masteries Core (Phase A, Slice 1 — content + definitions)

Spec: docs/specs/active/system-22-masteries-core.md. Scope = content/config/provider only (no DB/endpoints/combat).

### Enums (`src/ROTA.Domain/Enums/`)
| Enum | Values |
|------|--------|
| `MasteryAncient` | `Wrath=0, Bulwark=1, Hoard=2, Discernment=3` — the four pledgeable Ancients (level 1→5). |
| `MasteryActivityType` | `RaidHit=0, RaidDamageDealt=1, RaidKill=2, QuestNodeCleared=3, QuestBossCleared=4, GuildRaidContribution=5, GauntletRankEarned=6, LevelGained=7, EnergySpent=8, StaminaSpent=9, GoldEarned=10` — deterministic challenge-checklist counters (fed at chokepoints in Slice 4). |

### Config — `MasteryConfig` (`src/ROTA.Application/Configuration/MasteryConfig.cs`)
`IOptions<MasteryConfig>`, bound from appsettings `"MasteryConfig"`. `PledgeMultiplier` 2.0 (pledged = global×this),
`BulwarkMaxGuildDamagePercent` 1.0 (hard cap), `RespecGemCost` 150 (TUNE), `IncludeWeakestPillarFloor` false,
`BreadthMicroBonusPercent` 0.0 / `BreadthMicroBonusMaxPercent` 2.0, `SigilFindAppliesToFirstClear` false. Per-Ancient
per-level magnitudes live in `content/masteries.json`; this holds only scalar dials.

### Content models (`src/ROTA.Application/Models/AncientDefinition.cs`)
- `AncientDefinition { Ancient (MasteryAncient), Name, Theme, Description, double[] GlobalPercentByLevel (5), List<MasteryTierChallenge> TierChallenges (4), IconKey }`.
- `MasteryTierChallenge { int FromLevel (1..4), List<MasteryChallengeItem> Checklist }`.
- `MasteryChallengeItem { MasteryActivityType ActivityType, long Threshold }`.

### IMasteryDefinitionProvider (`src/ROTA.Application/Interfaces/`)
Singleton; eager-constructed in `Program.cs`; reads `content/masteries.json` at startup; throws `InvalidOperationException`
on invalid content (≠4 Ancients/duplicate; magnitude table length≠5/negative/non-decreasing; tier count≠4/fromLevel gap/
empty checklist/threshold≤0/unknown activityType; final tier not spanning ≥2 activity types). Methods: `GetAll()`,
`Get(MasteryAncient)`, `GlobalPercent(MasteryAncient, int level)`, `GetTierChallenge(MasteryAncient, int fromLevel)`.
Impl `MasteryDefinitionProvider` (`src/ROTA.Infrastructure/Services/`).

### Content (`src/ROTA.Api/content/masteries.json`)
4 Ancients. Global magnitude %/level: Wrath [0.5,1,1.5,2,2.5], Bulwark [0.1,0.2,0.3,0.4,0.5], Hoard & Discernment
[0.8,1.6,2.4,3.2,4.0]. Challenge checklists: early tiers single-system; T4→5 cross-system (raid + guild + gauntlet).

**Slice 1 scope:** content/validation only — no entities/migrations/endpoints/combat (Slices 2–7). +15 unit tests.

---

## System 16 — Gauntlet (Slice 1 — content + definitions)

### Enums (`src/ROTA.Domain/Enums/`)
| Enum | Values |
|------|--------|
| `GauntletLeague` | `Whelpling` (L1–1999), `Wyrm` (L2000–9999), `Dragon` (L10000+) |
| `GauntletEventState` | `Scheduled, Active, Closed, Settled` |
| `GauntletRewardKind` | `Tokens, Pitchfork, Trophy, Magic` |
| `GauntletTrophyTier` | `Aureate` (+25%), `Argent` (+10%), `Bronzed` (+5%) |
| `GauntletCurrency` | `Token, Pitchfork` — currency-ledger discriminator |
| `GauntletCurrencyTransactionType` | `RankReward, RaidDefeatReward, ShopPurchase, GemPurchase` |
| `StrikeTransactionType` | `RaidDefeat, GemPurchase, HitSpend, SpecialRaidDrop` |

### Config — `GauntletConfig` (`src/ROTA.Application/Configuration/GauntletConfig.cs`)
`IOptions<GauntletConfig>`, bound from appsettings `"GauntletConfig"`. LeagueBounds (Whelpling 1–1999 /
Wyrm 2000–9999 / Dragon 10000–`NoMaxLevel`), MinEntryLevel 20, PrizeRankCount 500, LeaderboardPageSize 200,
ScoreSnapshotSeconds 60, StrikeRatePerSize {Small 1, Medium 5, Large 20}, StrikesPerDefeat 10.
`NoMaxLevel = int.MaxValue` is the open-ended-top-league sentinel.

### Content models (`src/ROTA.Application/Models/`)
- `GauntletPrizeTable { List<GauntletPrizeBand> Bands }`; `GauntletPrizeBand { RankFrom, RankTo, Tokens, Pitchfork, TrophyId?, MagicId? }`.
- `GauntletTrophyDefinition { Id, Name, Tier (GauntletTrophyTier), LegionPowerBonusFraction (double) }`.
- `GauntletRaidDefinition { Id, Name, Tier="Event", LadderStage, BaseHp, TimerHours, StaminaCostPerHit, LootTableId, BaseGoldReward, BaseExperienceReward, BaseGemReward, ArtKey, GauntletScored }` — **dedicated** model (does NOT reuse `RaidDefinition`, leaving existing raid loading untouched).
- `MagicDefinition.OffCap` (bool, default false) — marks off-cap Gauntlet auras; combat reads it in Slice 4.

### IGauntletContentProvider (`src/ROTA.Application/Interfaces/`)
Singleton; mirrors `IMagicDefinitionProvider`. Loads `gauntlet_prizes/trophies/raids.json` at construction;
throws `InvalidOperationException` at startup on invalid data. Methods: `GetPrizeTable()`, `GetBandForRank(int)`,
`GetAllTrophies()`, `GetTrophyById(string)`, `GetGauntletRaids()`, `GetGauntletRaidByStage(int)`,
`ResolveLeague(int)`. Impl `GauntletContentProvider` (`src/ROTA.Infrastructure/Services/`) depends on
`IMagicDefinitionProvider` + `IOptions<GauntletConfig>`; eagerly constructed in `Program.cs` (fail-at-boot).

### Content (`src/ROTA.Api/content/`)
- `gauntlet_prizes.json` (7 bands, contiguous 1..500), `gauntlet_trophies.json` (3 trophies),
  `gauntlet_raids.json` (6-stage ladder, baseHp 5000→488281, ~2.5× geometric).
- `magics.json` +2 **off-cap** rows: `magic_wrath_of_the_ancients` (procChance 0.27, procAmount 2.50),
  `magic_blessing_of_the_ancients` (0.15, 4.25); both `offCap:true`, `gemPrice:0`.

**Startup validation:** duplicate ids; prize bands overlap/gap/coverage ≠ 1..PrizeRankCount; dangling
trophyId/magicId; trophy fraction ≤ 0; league bounds gap/overlap/non-open-ended-top; Gauntlet magic missing
or procChance ∉ (0,1] / procAmount ≤ 0 / offCap ≠ true; naming guard vs `magic_smite` / `magic_blessing_of_might`.

**Slice 1 scope:** content/validation only — NO entities, migrations, DbSets, endpoints, or combat changes
(deferred to Slices 2/4). 25 unit tests.

## System 16 — Gauntlet (Slice 2 — ledgers + lifecycle + join)

### Entities (`src/ROTA.Domain/Entities/`) + Fluent configs + migration `AddGauntletSystem`
- `GauntletEvent` { Id, Name, State(GauntletEventState), StartsAt, EndsAt, SettledAt?, +audit }. Guarded transitions (throw on illegal): `Create`→Scheduled, `Activate()`→Active, `Close()`→Closed, `MarkSettled()`→Settled. Index on `state`.
- `GauntletEntry` { Id, GauntletEventId, PlayerId, League(GauntletLeague, locked at create), Score(long), TieBreakAt, LastRank? }. `Create(eventId, playerId, league)`, `AddScore(delta, hitAt)`, `SetRank(int)`. Unique `(gauntlet_event_id, player_id)`; ranking index `(gauntlet_event_id, league, score)`.
- `StrikeTransaction` — **append-only ledger** { PlayerId, Amount(int), TransactionType(StrikeTransactionType), ReferenceId?, CreatedAt }. Balance=SUM. Unique partial idx `(player_id, transaction_type, reference_id) WHERE reference_id IS NOT NULL`.
- `GauntletCurrencyTransaction` — **append-only ledger** { ..., Currency(GauntletCurrency), ... }. Balance per currency=SUM WHERE currency. Unique partial idx `(player_id, currency, transaction_type, reference_id) WHERE reference_id IS NOT NULL`.
- `PlayerGauntletTrophy` { PlayerId, GauntletTrophyId } unique `(player_id, gauntlet_trophy_id)`; `PlayerEventMagic` { PlayerId, GauntletEventId, MagicDefinitionId } + `Revoke()`, unique triple; `PlayerMagicHonor` { PlayerId, MagicDefinitionId } unique pair.
- `ActiveRaid.GauntletEventId` (Guid?, additive) + `LinkGauntletEvent(eventId)`; nullable FK + index on `active_raids` (stamped at summon in Slice 4).
- `GemTransactionType.GauntletStrikePurchase = 11`; `GauntletConfig.StrikeGemPrice` (default 1, tunable). Result enums `StrikeSpendOutcome`/`GauntletCurrencySpendOutcome` { Charged, Insufficient, AlreadyCharged }.
- (Slice 6) `GauntletShopRewardKind { Unit, Legion, Gear, GemBundle, StrikeRefill }`; additive enum values `GemTransactionType.GauntletShopReward = 12` (GemBundle grant credit) + `StrikeTransactionType.ShopRefill = 4` (StrikeRefill credit) — code-only (stored as int, no migration/sentinel needed).
- **Migration `AddGauntletSystem`** — ONE consolidated migration (not the spec's 8). `dotnet ef database update` NOT run — coordinate with owner.

### Repositories (scoped)
- `IGauntletEventRepository`: `GetActiveAsync`, `GetMostRecentSettledAsync` (Slice 7: most recent Settled by SettledAt desc — drives the open hand-off), `FindByIdAsync`, `CreateAsync`, `UpdateAsync`.
- `IGauntletEntryRepository`: `FindByEventAndPlayerAsync`, `GetForEventAsync`, `UpsertAsync`.
- `IActiveRaidRepository` (Slice 7 add): `GetGauntletStagesForPlayerAsync(playerId, gauntletEventId)` — all of a player's gauntlet ladder raids for an event (any state), used to derive the current stage / next stage to spawn.
- `IStrikeRepository`: `GetBalanceAsync`, `CreateAsync`, `ReferenceExistsAsync`, `SpendAsync(playerId, amount, referenceId)`→`StrikeSpendOutcome` (idempotency-first + unique-violation backstop).
- `IGauntletCurrencyRepository`: `GetBalanceAsync(playerId, currency)`, `CreateAsync`, `ReferenceExistsAsync`, `SpendAsync(playerId, currency, amount, referenceId)`→`GauntletCurrencySpendOutcome`.
- `IPlayerGauntletTrophyRepository`: `GetForPlayerAsync`, `UpsertAsync`. `IPlayerEventMagicRepository`: `FindAsync`, `GrantAsync`, `RevokeAllForEventAsync`. `IPlayerMagicHonorRepository`: `HasHonorAsync`, `GrantAsync`.

### Services
- `IGauntletService`: `GetCurrentEventAsync`, `JoinEventAsync(playerId)` (league locked via `ResolveLeague`; rejects no-event/L<MinEntry/banned/deleted; idempotent), `GetMyEntryAsync(playerId, eventId)`, `GetLadderAsync(playerId)` (Slice 7: the auto-advancing ladder target — returns the player's active gauntlet stage, else lazily spawns the next stage above the highest defeated (Personal, GauntletEventId-stamped, MaxHp = stage baseHp, no difficulty mult); `NoActiveEvent`/`JoinedRequired`/`Complete` flags; progress DERIVED from gauntlet ActiveRaids — no entity/migration; reuses RaidService.GetRaidByIdAsync for the projection), `BuyStrikesAsync(playerId, strikes, idempotencyKey)` (gem spend → strike credit; referenceId `strikebuy:{playerId}:{key}`; lost-purchase recovery), `GetShopAsync(playerId)` (Slice 6: catalogue + Token/Pitchfork balances + per-entry AlreadyOwned), `BuyFromShopAsync(playerId, shopEntryId)` (Slice 6: ownership pre-check → tri-state currency spend → idempotent grant; refId `gauntletshop:{playerId}:{shopEntryId}`).
- `IGauntletAdminService`: `OpenEventAsync` (≤1 active; Slice 7: also hands off the most-recently-settled event's rank winners their per-event consumable — rank-1 Wrath, ranks 2–10 Blessing — scoped to the NEW event, idempotent via FindAsync pre-check + GrantAsync; this is the deferred "spec step 2e"), `CloseEventAsync` (must be Active), `SettleEventAsync` (Slice 5: idempotent prize payout; never grants the next-event consumable — that is the open hand-off).
- `IGauntletScoringService` (Slice 3): `UpdateScoreAsync(playerId, eventId, deltaScore, hitAt)` (atomic score += delta; tie_break_at advances only on positive delta; wired by S4), `RecomputeRanksAsync(eventId)` (per-league `ROW_NUMBER` snapshot into last_rank; idempotent), `GetLeaderboardAsync(eventId, league, callerId)` → `GauntletLeaderboardResponse` (top `LeaderboardPageSize` by snapshot rank + caller's league-scoped rank/score + total ranked).

### Background services
- `GauntletRankSnapshotService` (hosted, singleton; DI scope per tick): every `GauntletConfig.ScoreSnapshotSeconds` resolves the active event and calls `RecomputeRanksAsync`; no-op when none active; try/catch never crashes the host.

### Endpoints
- `GauntletController` [Authorize]: `GET /api/gauntlet` (overview: event + entry + strike/token/pitchfork balances), `GET /api/gauntlet/ladder` (Slice 7: the auto-advancing ladder target — always 200; flags NoActiveEvent/JoinedRequired/Complete or the current ActiveRaid), `GET /api/gauntlet/leaderboard?league=` (Slice 3: snapshot-ranked board + `YourRank`/`YourScore`; 400 invalid/missing league; empty board when no active event), `POST /api/gauntlet/join`, `POST /api/gauntlet/strikes/buy`, `GET /api/gauntlet/shop` (Slice 6: catalogue + balances), `POST /api/gauntlet/shop/{entryId}/buy` (Slice 6: Success/AlreadyCharged → 200, AlreadyOwned → 409, InsufficientTokens → 422, unknown entry → 404).
- `GauntletAdminController` [AdminOnly + DB actor re-verify]: `POST /api/admin/gauntlet/events` (open; 409 on ≤1-active), `POST .../events/{id}/close`, `POST .../events/{id}/settle`.
- CLI (`AdminCli`): `gauntlet-open`, `gauntlet-close`, `gauntlet-settle`.

### DTOs (`src/ROTA.Shared/DTOs/GauntletDTOs.cs`)
GauntletEventResponse, GauntletEntryResponse, GauntletOverviewResponse, StrikeBalanceResponse, GauntletCurrencyBalanceResponse, BuyStrikesRequest, OpenGauntletEventRequest, JoinGauntletResult, BuyStrikesResult, GauntletEventActionResult, GauntletLadderResponse (Slice 7: ActiveRaid?/CurrentStage/StageCount/Complete/JoinedRequired/NoActiveEvent), GauntletLeaderboardResponse (Slice 3: League/Entries/YourRank/YourScore/TotalRanked), GauntletLeaderboardEntryDto (Rank/PlayerId/DisplayName/Score), GauntletShopEntryResponse + GauntletShopResponse + BuyShopResult (Slice 6). Validators: BuyStrikesRequestValidator, OpenGauntletEventRequestValidator.

**Slice 2 scope:** persistence + lifecycle + join + strike economy. No combat changes (scoring + strike spend wired in Slice 4). +38 unit, +10 integration tests.

## System 16 — Gauntlet (Slice 4 — combat integration, DEEP)

Five amplifiers wired into `RaidService.HitRaidAsync` with **NO parallel combat path** (a trophy-less,
non-Gauntlet hit is byte-for-byte identical to before). New scoped ctor deps:
`IPlayerGauntletTrophyRepository`, `IGauntletContentProvider`, `IPlayerEventMagicRepository`,
`IPlayerMagicHonorRepository`, `IStrikeRepository`, `IGauntletScoringService`, `IOptions<GauntletConfig>`.
- **(A) Trophy multiplier** — inside the active-legion block: `rawLegionPower *= 1 + Max(ownedTrophy.LegionPowerBonusFraction)` (highest-only, NOT additive) **before** `PowerScaling`. Applies to EVERY raid. No trophies → ×1.0; legion-less players skip the query.
- **(B) Off-cap auras** — Gauntlet raids only (`GauntletEventId != null`): for each `MagicDefinition.OffCap`, current owner (`PlayerEventMagic` for the event) ×1.25 / former owner (`PlayerMagicHonor`) ×1.10 / neither → no aura; roll `min(1, procChance×mult)`, add `procAmount×mult×preProc` to `damageFinal` **before crit**. NEVER folded into the `MaxAggregateProcBonus` cap.
- **(C) Strike fork** — Gauntlet hits spend **Strikes** (cost by hit size via `GauntletConfig.StrikeRatePerSize`; refId `strikespend:{activeRaidId}:{idempotencyKey}`) instead of Stamina, inside the advisory-lock tx; insufficient → 422 `InsufficientStrikes`. `StrikeRepository.SpendAsync` reimplemented **tx-safe** (raw Npgsql, ambient-tx aware, EXISTS + balance-guarded INSERT, no `ChangeTracker.Clear`).
- **(D) Score update** — after the leaderboard hook: `IGauntletScoringService.UpdateScoreAsync(playerId, GauntletEventId, damageFinal, now)` (rides the ambient tx; no-op if unjoined). Non-Gauntlet hits never call it.
- DTOs: `RaidHitResponse.OffCapAuraBonus` + `NewStrikeBalance` (0 on non-Gauntlet); `RaidHitFailureCode.InsufficientStrikes = 8` → 422.

**Known follow-up (NOT in S1–S6) — RESOLVED in Slice 7:** the Gauntlet **ladder summon/climb** endpoint +
gauntlet-stage definition resolution. Slice 4 was exercised via seeded ActiveRaids with `GauntletEventId`
set + a normal `RaidDefinitionId`; Slice 7 makes a real `gauntlet_stage_N` def resolve through
`IRaidDefinitionProvider` (see Slice 7 below) and adds the auto-advancing ladder endpoint.

## System 16 — Gauntlet (Slice 5 — settlement, idempotent)

`GauntletAdminService.SettleEventAsync(eventId)` — idempotent prize distribution (money-bug class).
Already-Settled → zero-count no-op; must be Closed otherwise. Flow: `RecomputeRanksAsync` → for each entry
with `LastRank ≤ PrizeRankCount`, `GetBandForRank` → credit **Tokens** (refId
`gauntletsettle:{eventId}:{playerId}:tokens`) + **Pitchfork** (`…:pitchfork`) via the currency ledger
(ReferenceExists pre-check + unique partial index) + **Trophy** (`PlayerGauntletTrophy.UpsertAsync`,
idempotent) → **honor write-back** (`RevokeAllForEventAsync` returns the revoked rows → write
`PlayerMagicHonor` per holder if absent) → `MarkSettled` (only after all grants commit). Ranks are
per-league, so every league's rank-1..N gets its own band. Returns `GauntletSettlementSummaryResponse`
{RanksSettled, TokensGranted, PitchforkGranted, TrophiesGranted, HonorsWritten} (carried on
`GauntletEventActionResult.Settlement`; surfaced by the settle endpoint + `gauntlet-settle` CLI — no API contract change).

**Per-raid-defeat reward** — in `HitRaidAsync`'s `isKill` block, gated on `GauntletEventId`, inside the
advisory-lock tx: `+StrikesPerDefeat` strikes (refId `gauntletdefeat:{raidId}:{playerId}:strikes`) + `+1`
Token (`…:token`), each ReferenceExists-guarded (idempotent on a re-processed kill). New RaidService dep:
`IGauntletCurrencyRepository`.

**DEFERRED (follow-up) — RESOLVED in Slice 7:** spec step 2e — the next-event Wrath/Blessing
**consumable** (`PlayerEventMagic`) hand-off now runs in `OpenEventAsync` (the next event exists at open;
settle still writes only honor + tokens + pitchfork + trophies). +19 unit, +5 integration tests (incl. settle-twice-pays-once vs real ledger balances).

## System 16 — Gauntlet (Slice 6 — token shop, economy idempotency)

**Content** — `content/gauntlet_shop.json` + `GauntletShopEntry` model `{ Id, RewardKind, PayloadId,
Amount, Currency (Token|Pitchfork), Price, MaxOwned? }` (own-once kinds set `MaxOwned = 1`;
GemBundle/StrikeRefill are repeatable, `MaxOwned = null`). Starter catalogue (6 entries, real def ids):
`shop_unit_morvath` (Unit gen_morvath, Token 120), `shop_legion_ironlegion` (Legion legion_ironlegion,
Token 200), `shop_gear_pano_cuirass` (Gear gear_pano_cuirass, Token 80), `shop_gear_pano_steed` (Gear
gear_pano_steed, **Pitchfork** 15), `shop_gembundle_small` (GemBundle 250 gems, Token 50),
`shop_strikerefill_medium` (StrikeRefill 50 strikes, Token 25).

**Provider** — `IGauntletShopProvider` / `GauntletShopProvider` (Infrastructure singleton, eagerly
constructed in `Program.cs`). Ctor takes the unit/legion/gear def providers for payloadId referential
validation. Startup throws on: empty catalogue; empty/duplicate id; invalid currency/rewardKind; `price ≤ 0`;
Unit/Legion/Gear payloadId that does not resolve in the matching def provider OR is empty OR is not
`maxOwned:1`; GemBundle/StrikeRefill `amount ≤ 0`.

**Purchase flow** — `GauntletService.BuyFromShopAsync(playerId, shopEntryId)` mirrors
`LegionService.BuyUnitAsync`: (1) unknown id → NotFound-style fail; (2) own-once kinds — ownership
pre-check (via `ILegionService.GetOwnedUnitsAsync`/`GetOwnedLegionsAsync` /
`IEquipmentService.GetOwnedGearAsync`) → `AlreadyOwned` WITHOUT charge/grant; (3)
`IGauntletCurrencyRepository.SpendAsync(playerId, entry.Currency, entry.Price, refId)` (refId
`gauntletshop:{playerId}:{shopEntryId}`) — `Insufficient` → `InsufficientTokens` (no write),
`Charged|AlreadyCharged` → grant; (4) grant dispatched on rewardKind, each idempotent so AlreadyCharged
re-grants without re-charging: Unit/Legion → `Grant*Async` (own-once upsert), Gear → `GrantGearAsync(..,1)`
(gated by the step-2 pre-check), GemBundle → `GrantGemsAsync(.., GauntletShopReward, refId)` (gem-ledger
unique index), StrikeRefill → `StrikeTransaction.Create(.., ShopRefill, refId)` guarded by
`ReferenceExistsAsync`. Audited (`GauntletShopBuy`). **Token vs Pitchfork isolation:** the spend passes
`entry.Currency`, so a Pitchfork-priced entry attempted with only a Token balance → `Insufficient` in
Pitchfork → `InsufficientTokens` (the Token balance is never touched).

**New `GauntletService` ctor deps:** `IGauntletShopProvider`, `ILegionService`, `IEquipmentService`.
+11 unit (GauntletService shop) + 7 unit (GauntletShopProvider validation) + 2 integration
(`GauntletShopIdempotencyTests`: buy-twice-charges-once + currency isolation vs real Postgres ledgers).

## System 16 — Gauntlet (Slice 7 — loop completion: ladder + hand-off)

Makes the Gauntlet **end-to-end playable**. **Zero combat-formula change** — `HitRaidAsync` is untouched.

**(1) Gauntlet-stage definition resolution (no `HitRaidAsync` change).** `RaidDefinitionProvider` now ALSO
loads `content/gauntlet_raids.json` and maps each `GauntletRaidDefinition` → a plain `RaidDefinition`
(fields overlap; `lootTableId` is `""` so `DistributeKillRewardsAsync`'s loot pass is benign), registered
in the same id→def dictionary. So `HitRaidAsync`'s `_raidDefinitions.GetById(raid.RaidDefinitionId) ?? throw`
(line ~447) resolves a `gauntlet_stage_N` raid **unchanged**. Throws at startup on a stage-id collision with
a `raids.json` id. The Gauntlet combat behaviour (Strikes/auras/score/defeat-reward) stays gated on
`ActiveRaid.GauntletEventId`, NOT on the definition — so mapping the stage does not add any combat branch.

**(2) Ladder summon / auto-advance** — `IGauntletService.GetLadderAsync(playerId)`: resolve active event
(none → `NoActiveEvent`); require a joined `GauntletEntry` (else `JoinedRequired`); read the player's gauntlet
ladder raids (`IActiveRaidRepository.GetGauntletStagesForPlayerAsync(playerId, eventId)`); if an ACTIVE (not
defeated, not expired) stage exists → return it; else `nextStage = (highest DEFEATED stage) + 1` (1 if none) —
if `nextStage > stageCount` → `Complete`, else **lazily spawn** `ActiveRaid.Create(raidDefinitionId =
"gauntlet_stage_{n}", maxHp = stage.BaseHp, Personal, Normal)` + `LinkGauntletEvent(eventId)`, `ExpiresAt =
event.EndsAt`, persist + audit (`GauntletLadderSpawn`), return it. **Auto-advance** = the next stage is spawned
on the next call after a defeat, so the player never manually summons. Stage number parsed from
`RaidDefinitionId` (`gauntlet_stage_N` → N). **NO new entity / NO migration** — progress is derived from the
ActiveRaids. The `ActiveRaidResponse` projection reuses `RaidService.GetRaidByIdAsync` (the spawned stage is
a join-by-id case: active + Personal + summoner = caller). New `GauntletService` ctor deps:
`IActiveRaidRepository`, `IRaidService`. Endpoint: `GET /api/gauntlet/ladder` [Authorize].

**(3) Regular-list exclusion** — `RaidService.GetActiveRaidsAsync` now excludes raids with
`GauntletEventId != null` (gauntlet stages are Personal + caller-owned, so the own-raids branch would
otherwise surface them); they are reached only via `/api/gauntlet/ladder`. Join-by-id is unaffected.

**(4) Cross-event rank-magic consumable hand-off** — `GauntletAdminService.OpenEventAsync`, after creating +
activating the new event, calls a private `HandOffRankMagicsAsync(newEventId)`:
`IGauntletEventRepository.GetMostRecentSettledAsync()` → for each of its entries with `LastRank != null` whose
`GetBandForRank` band has a non-null `MagicId` → `IPlayerEventMagicRepository.GrantAsync(playerId, newEventId,
magicId)`. Idempotent: `FindAsync` pre-check + `GrantAsync` is itself idempotent. So prior rank-1 holders are
**current Wrath owners** (×1.25) and ranks 2–10 **current Blessing owners** for the new event (Slice-4 combat
reads `PlayerEventMagic` for the current event). This is the deferred spec step 2e — it belongs at OPEN (the
next event exists), not at settle (auto-settle-on-close runs before the next event exists). Audited in the
`GauntletEventOpen` entry (count of winners handed off).

+10 unit (7 `GetLadderAsync` in `GauntletServiceTests`, 3 open-hand-off in `GauntletAdminServiceTests`) +
6 integration (`GauntletLadderTests`: gauntlet hit resolves end-to-end + spends strikes + moves score; defeat
reward; list exclusion; ladder spawn-then-return-same; not-joined; open hand-off vs real Postgres). No new migration.

**Finite-6-stage ceiling is TUNABLE** — the ladder length is `content/gauntlet_raids.json`'s stage count
(currently 6 rising-HP stages). Deeper climbs = add stages (or formula-extend HP) in JSON; no code change.

---

## System 21 — Guild / Clan Foundations (Slice 1)

Identity + membership + join flow + roles/permissions + lifecycle. No guild chat (S2) or guild raids (S3).
Migration `AddGuildSystem` (3 tables: `guilds`, `guild_memberships`, `guild_join_requests`).

### Enums (`src/ROTA.Domain/Enums/`)
| Enum | Values | Notes |
|------|--------|-------|
| `GuildRank` | `Member=1, Officer=2, Leader=3` | Permission checks compare by int value. No 0 value → NO store default on the EF int column. |
| `GuildJoinPolicy` | `Open=0, Application=1, InviteOnly=2` | Per-guild choice; Open = auto-accept to cap. |
| `GuildJoinRequestKind` | `Application=0, Invite=1` | Application = player→guild; Invite = officer→player. |
| `GuildJoinRequestStatus` | `Pending=0, Accepted=1, Rejected=2, Withdrawn=3, Expired=4` | |

### Config (`src/ROTA.Application/Configuration/GuildConfig.cs`)
Bound from appsettings `"GuildConfig"` via `IOptions<GuildConfig>`. `MemberCap` 50, `CreationGoldCost`
25000 (**TUNABLE — flagged**), `MinCreationLevel` 20, `LeaderInactivityDays` 14, `TagMinLength` 2,
`TagMaxLength` 5, `NameMaxLength` 32.

### Entities (`src/ROTA.Domain/Entities/`)
- **`Guild`** — `Id, Name, NameNormalized, Tag, TagNormalized, Description, CrestId?, LeaderId, Motd,
  MemberCap, Level, Xp, MemberCount, JoinPolicy, +created/updated/IsDeleted`. Methods: `Create`, `Rename`,
  `SetTag`, `SetDescription`, `SetMotd`, `SetCrest`, `SetJoinPolicy`, `SetLeader`, `AddXp`,
  `IncrementMemberCount`/`DecrementMemberCount`, `Disband`, static `Normalize(string)`. Name/Tag uniqueness
  is enforced case-insensitively via the lowercase shadow columns `NameNormalized`/`TagNormalized` under
  **partial unique indexes** (`HasFilter("is_deleted = false")`) — reuse after disband works.
- **`GuildMembership`** — `Id, GuildId, PlayerId, Rank, ContributionTotal, JoinedAt, LastActiveAt,
  +created/updated/IsDeleted`. **Partial unique index on `player_id WHERE is_deleted=false`** (one active
  guild per player; re-joinable after leave). Methods: `Create(guildId, playerId, rank)`, `SetRank`,
  `AddContribution`, `TouchActivity`, `SoftDelete`.
- **`GuildJoinRequest`** — `Id, GuildId, PlayerId, Kind, Status, +created/updated/IsDeleted`. Methods:
  `Create(guildId, playerId, kind)`, `Accept`, `Reject`, `Withdraw`, `Expire`.
- **`Player`** additive methods: `JoinGuild(Guid, GuildRank)`, `LeaveGuild()`, `SetGuildRank(GuildRank)` —
  keep the denormalized `Player.GuildId`/`GuildRank` in sync (membership row is the source of truth).

### Repositories (`Application/Interfaces` + `Infrastructure/.../Repositories/GuildRepositories.cs`, scoped)
- **IGuildRepository** — `FindByIdAsync`, `FindByNameAsync`(ci), `FindByTagAsync`(ci),
  `NameExistsAsync`/`TagExistsAsync`(ci, optional exclude id), `CreateAsync`, `UpdateAsync`,
  `BrowseAsync(query,page,pageSize)` → `GuildBrowseEntry`, `GetRosterAsync(guildId)` → `GuildRosterEntry`.
- **IGuildMembershipRepository** — `FindByPlayerAsync`, `FindByGuildAndPlayerAsync`, `GetForGuildAsync`,
  `CountActiveAsync`, `CreateAsync`, `UpdateAsync`.
- **IGuildJoinRequestRepository** — `FindByIdAsync`, `GetPendingForGuildAsync`, `GetPendingForPlayerAsync`,
  `FindPendingAsync(guildId, playerId, kind)`, `CreateAsync`, `UpdateAsync`.

### IGuildService (`src/ROTA.Application/Interfaces/IGuildService.cs`)
Impl: `GuildService` (`src/ROTA.Application/Services/GuildService.cs`). Server-authoritative; every state
change writes audit_log. Permission rule for kick/promote/demote: **actor.Rank > target.Rank**; promote/
demote additionally require **resulting rank < actor.Rank** (so in the 3-tier ladder only the Leader changes
ranks — officers can't create officers). Leader-only: disband, rename/tag/description/join-policy, transfer.
Officer+: invite, accept/reject application, set MOTD.

| Method | Description |
|--------|-------------|
| `CreateGuildAsync(playerId, name, tag, description, joinPolicy)` | Gates: not already in guild, level ≥ MinCreationLevel, gold ≥ CreationGoldCost, name/tag free (ci) + not reserved + tag length. Deducts gold, creates Guild (Leader, count 1) + Leader membership, syncs Player. → `CreateGuildResult`. |
| `DisbandGuildAsync(playerId, guildId)` | Leader-only. Soft-deletes guild + all memberships; releases every member's Player fields. |
| `UpdateGuildAsync(actorId, guildId, name?, tag?, description?, motd?, joinPolicy?)` | Leader for name/tag/desc/policy; officer+ for MOTD. Re-validates ci uniqueness/reserved/length on name/tag. |
| `ApplyAsync(playerId, guildId)` | Open → auto-join (cap-checked); Application → pending request (idempotent); InviteOnly → reject. → `ApplyGuildResult` (Joined / RequestId). |
| `AcceptApplicationAsync` / `RejectApplicationAsync(actorId, guildId, requestId)` | Officer+. Accept → membership (cap + still-guild-less checks) + mark Accepted. |
| `InviteAsync(actorId, guildId, targetUsernameOrId)` | Officer+. Target guild-less → pending Invite (idempotent). |
| `AcceptInviteAsync(playerId, requestId)` | Invited player accepts → membership (cap + still-guild-less). |
| `LeaveAsync(playerId, guildId)` | Member/officer leaves (Leader cannot — transfer/disband first). |
| `KickAsync(actorId, guildId, targetPlayerId)` | actor.Rank > target.Rank. |
| `PromoteAsync` / `DemoteAsync(actorId, guildId, targetPlayerId)` | Member↔Officer within the permission rule; syncs Player rank. |
| `TransferLeadershipAsync(leaderId, guildId, targetPlayerId)` | Leader-only; target→Leader, old leader→Officer; `Guild.SetLeader`. |
| `RunInactivitySuccessionAsync(guildId)` | If leader's `LastActiveAt` older than LeaderInactivityDays, promote most-active officer (LastActiveAt desc, ContributionTotal tiebreak). Triggerable now; **scheduled auto-driver is a FOLLOW-UP**. |
| `GetGuildAsync(guildId, callerId)` | Detail + roster; pending requests included for officer+ callers. → `GuildDetailResponse?`. |
| `BrowseGuildsAsync(query, page)` | Searchable paged guild list. → `GuildSummaryDto[]`. |

### DTOs (`src/ROTA.Shared/DTOs/GuildDTOs.cs`)
`GuildFailureCode` (None/NotFound/Validation/AlreadyInGuild/NotInGuild/NameTaken/TagTaken/InsufficientLevel/
InsufficientGold/MemberCapReached/PermissionDenied/PolicyForbidsApply/LeaderCannotLeave/Conflict);
`GuildActionResult`, `CreateGuildResult`, `ApplyGuildResult`; requests `CreateGuildRequest`,
`GuildInviteRequest`, `UpdateGuildRequest`, `TransferLeadershipRequest`; responses `GuildSummaryDto`,
`GuildMemberDto`, `GuildJoinRequestDto`, `GuildDetailResponse`. Validators in
`src/ROTA.Application/Validators/GuildValidators.cs` (reuse `ReservedUsernames`).

### GuildController — `api/guilds` [Authorize]
`src/ROTA.Api/Controllers/GuildController.cs`. PlayerId from JWT sub. Failure codes → 400/403/404/409.

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/guilds?query=&page=` | `BrowseGuildsAsync` | 200 |
| `POST /api/guilds` | `CreateGuildAsync` | 201, 400, 409 |
| `GET /api/guilds/{id}` | `GetGuildAsync` | 200, 404 |
| `PUT /api/guilds/{id}` | `UpdateGuildAsync` | 200, 400, 403, 404, 409 |
| `POST /api/guilds/{id}/disband` | `DisbandGuildAsync` | 200, 403, 404 |
| `POST /api/guilds/{id}/apply` | `ApplyAsync` | 200, 404, 409 |
| `POST /api/guilds/{id}/requests/{reqId}/accept` | `AcceptApplicationAsync` | 200, 403, 404, 409 |
| `POST /api/guilds/{id}/requests/{reqId}/reject` | `RejectApplicationAsync` | 200, 403, 404, 409 |
| `POST /api/guilds/{id}/invite` | `InviteAsync` | 200, 400, 403, 404, 409 |
| `POST /api/guilds/invites/{reqId}/accept` | `AcceptInviteAsync` | 200, 403, 404, 409 |
| `POST /api/guilds/{id}/leave` | `LeaveAsync` | 200, 409 |
| `POST /api/guilds/{id}/members/{playerId}/kick` | `KickAsync` | 200, 403, 404 |
| `POST /api/guilds/{id}/members/{playerId}/promote` | `PromoteAsync` | 200, 403, 404, 409 |
| `POST /api/guilds/{id}/members/{playerId}/demote` | `DemoteAsync` | 200, 403, 404, 409 |
| `POST /api/guilds/{id}/transfer` | `TransferLeadershipAsync` | 200, 403, 404, 409 |

### Guild chat (Slice 2) — additive to the existing `ChatHub`
Real-time over SignalR `ChatHub` (`/hubs/chat`); world/raid chat unchanged. The caller is in ≤1 guild via
`Player.GuildId`; the guild group is always resolved server-side from the verified identity, never from a
client-supplied id. `Scope="Guild"` on the shared `ChatMessageDto`. // BETA

| Hub method (`src/ROTA.Api/SignalR/ChatHub.cs`) | Behavior |
|---|---|
| `JoinGuildChannel()` | Resolves caller's `GuildId`; null → `GuildChatUnavailable` to caller; else adds connection to group `guild:{guildId}`. |
| `LeaveGuildChannel()` | Removes connection from `guild:{guildId}` (no-op if not in a guild). |
| `SendGuildMessage(string body)` | **Mute-gate** (banned/muted → `Muted`, like world/raid) then **member-gate** (null `GuildId` → `GuildChatUnavailable`); on pass appends to the per-guild ring buffer + broadcasts `GuildMessage` to `guild:{guildId}`. Reuses world-chat trim + 500-char cap. |

| Store / Endpoint | Notes |
|---|---|
| `IGuildChatStore` + `RedisGuildChatStore` | Per-guild 100-msg ring buffer, key `chat:guild:{guildId}` (LPUSH→LTRIM, read oldest→newest). Mirrors `RedisWorldChatStore` + a `guildId` arg → isolated per guild. Scoped DI. |
| `GET /api/chat/guild/history?count=` `[Authorize]` (`ChatController`) | Caller's `GuildId` from JWT sub; member-gated → null GuildId returns 200 + empty list (mirrors world-history). |

---

## System 17 — Global Leaderboards (Slice 1)

### Enums (`src/ROTA.Domain/Enums/`)

| Enum | Values | Notes |
|------|--------|-------|
| `LeaderboardBoard` | `StatAttack(0), StatDefense(1), StatDiscernment(2), EnergySpent(3), DamageDealt(4), LargestHit(5)` | Flat: three per-stat live ladders + three accumulation boards. |
| `LeaderboardPeriod` | `Live(0), Daily(1), Weekly(2), Monthly(3)` | Window granularity. `Live` = Stat snapshot. |
| `LeaderboardAggregation` | `Sum(0), Max(1)` | How contributions fold into entry value. Stat boards overwrite (neither). |

### Config (`src/ROTA.Application/Configuration/LeaderboardConfig.cs`)

Bound from `appsettings.json` section `"LeaderboardConfig"` via `IOptions<LeaderboardConfig>`. All defaults are the LOCKED spec values. Startup validation throws `InvalidOperationException` on:
- `Timezone != "UTC"`
- `MinLevel < 1`
- `PageSize < 1`
- Unrecognised `WeekStartsOn` value

### IPeriodKeyResolver
`src/ROTA.Application/Interfaces/IPeriodKeyResolver.cs`

| Method | Description |
|--------|-------------|
| `string Resolve(DateTimeOffset utcNow, LeaderboardPeriod period)` | Returns deterministic `period_key` string. `Live`→`"live"`, `Daily`→`"day:yyyy-MM-dd"`, `Weekly`→`"week:yyyy-Www"` (ISO), `Monthly`→`"month:yyyy-MM"`. Always converts input to UTC first. |

Implementation: `PeriodKeyResolver` (`src/ROTA.Application/Services/PeriodKeyResolver.cs`). Registered as singleton. Validates config in constructor. Uses `System.Globalization.ISOWeek` for year-boundary-correct ISO week numbers.

---

## System 17 — Global Leaderboards (Slice 2)

### LeaderboardEntry (Entity)
`src/ROTA.Domain/Entities/LeaderboardEntry.cs`

One aggregate row per `player × board × period_key`. Sum boards call `AddValue`; Max boards call `MaxValue`. Stat boards (Period=Live) overwrite via repository upsert.

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `Guid` | PK |
| `PlayerId` | `Guid` | FK → players |
| `Board` | `LeaderboardBoard` | Stored as `int`, no HasDefaultValue |
| `Period` | `LeaderboardPeriod` | Stored as `int`, no HasDefaultValue |
| `PeriodKey` | `string` | Deterministic calendar bucket — "day:yyyy-MM-dd", "week:yyyy-Www", "month:yyyy-MM", "live" |
| `Value` | `long` | Accumulated sum or best-max |
| `LastProgressAt` | `DateTimeOffset` | Tiebreak: earliest-to-reach wins on equal value |
| `Rank` | `int?` | Denormalized snapshot rank; null in v1 (RankRefresh=OnRead) |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |
| `IsDeleted` | `bool` | |

Domain methods:

| Method | Description |
|--------|-------------|
| `Create(playerId, board, period, periodKey, initialValue, at)` | Factory — sets all fields, `LastProgressAt = at` |
| `AddValue(long delta, DateTimeOffset at)` | Sum board: `Value += delta`; moves `LastProgressAt` and `UpdatedAt`. Zero/negative delta is a no-op (guarded). |
| `MaxValue(long candidate, DateTimeOffset at) → bool` | Max board: updates `Value` only when `candidate > Value`; `LastProgressAt` only moves when it actually raised. Returns `true` if raised. |
| `SetRank(int)` | Stores denormalized rank (snapshot mode, reserved for future use). |

EF config: table `leaderboard_entry`; snake_case; enums stored as `int` with NO `HasDefaultValue` (no sentinel required — see note below); three indexes:
- `ix_leaderboard_entry_player_id` (FK index, required by arch rules)
- `ix_leaderboard_entry_upsert_key` UNIQUE on `(player_id, board, period_key)` — the ON CONFLICT target
- `ix_leaderboard_entry_board_period_value` on `(board, period_key, value)` — drives ranked reads

Migration: `AddLeaderboardEntry` (applied by owner).

**EF enum / HasDefaultValue note:** `Board` and `Period` columns have NO `HasDefaultValue` in the Fluent config. The `HasSentinel` rule applies only when `HasDefaultValue` assigns a non-zero default that could collide with the CLR zero-default (the RaidSize/PlayerRoles bug). Here both zero-values (`StatAttack=0`, `Live=0`) are legitimate written values, and the `Create` factory always sets them explicitly — no store default exists to fight.

---

### ILeaderboardEntryRepository
`src/ROTA.Application/Interfaces/ILeaderboardEntryRepository.cs`

| Method | Description |
|--------|-------------|
| `Task IncrementAsync(playerId, board, period, periodKey, delta, at, ct)` | Race-safe upsert+add for Sum boards. `INSERT … ON CONFLICT (player_id, board, period_key) DO UPDATE SET value = value + EXCLUDED.value`. Concurrent increments serialised at the DB row level — no lost updates. |
| `Task MaxUpdateAsync(playerId, board, period, periodKey, candidate, at, ct)` | Race-safe upsert+max for Max boards. `GREATEST(stored, candidate)` on conflict; `last_progress_at` only moves when value actually raised (CASE expression). |
| `Task<IReadOnlyList<LeaderboardEntry>> GetPageAsync(board, periodKey, page, pageSize, ct)` | Ordered `value DESC, last_progress_at ASC`; excludes `is_deleted`; offset/limit paged. |
| `Task<LeaderboardEntry?> GetPlayerEntryAsync(playerId, board, periodKey, ct)` | Single row via the unique index; `null` if absent. |

Implementation: `LeaderboardEntryRepository` (`src/ROTA.Infrastructure/Persistence/Repositories/LeaderboardEntryRepository.cs`). Uses raw `NpgsqlCommand` for the upsert paths (same pattern as `BetaKeyRepository.TryRedeemAsync`). Participates in ambient EF transactions when one is open (advisory-lock path from `AtomicApplyHitAsync`).

**Slice 3 additions to `ILeaderboardEntryRepository`:**

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<EligibleLeaderboardEntry>> GetEligiblePageAsync(board, periodKey, page, pageSize, minLevel, excludeAdmins, ct)` | Eligibility-aware ranked page. JOINs players; applies `is_deleted=false AND is_banned=false AND level>=minLevel AND (excludeAdmins? roles&4=0)`. Ordered `value DESC, last_progress_at ASC`. |
| `Task<int> CountEligibleAsync(board, periodKey, minLevel, excludeAdmins, ct)` | Count of eligible entries for a board+period (for `TotalRanked`). Same eligibility predicate. |
| `Task<CallerRankEntry?> GetCallerRankAsync(callerId, board, periodKey, minLevel, excludeAdmins, ct)` | Returns the caller's value + rank (1-based, counting eligible entries strictly above). `null` if caller has no entry or is ineligible. |

---

## System 17 — Global Leaderboards (Slice 3)

### ILeaderboardService
`src/ROTA.Application/Interfaces/ILeaderboardService.cs`

| Method | Description |
|--------|-------------|
| `Task<List<LeaderboardSummary>> GetBoardsAsync(ct)` | Discovery list: all boards with supported periods and current period_key. |
| `Task<LeaderboardPageResult> GetPageAsync(board, period, periodKey?, page, callerId, ct)` | Ranked page + caller rank. Validates board/period combo; resolves current period_key if none supplied. Returns `LeaderboardPageResult.Fail(msg)` on bad input (caller maps to 400). |

Implementation: `LeaderboardService` (`src/ROTA.Application/Services/LeaderboardService.cs`). Delegates to `ILeaderboardEntryRepository` for eligible paged reads and caller rank. `PlayerId` from JWT sub.

### DTOs (added Slice 3)
`src/ROTA.Shared/DTOs/LeaderboardDTOs.cs`

- `LeaderboardEntryDto` — `Rank`, `PlayerId`, `DisplayName`, `Value`
- `LeaderboardPageResponse` — `Board`, `Period`, `PeriodKey`, `Page`, `PageSize`, `TotalRanked`, `Entries`, `You`
- `LeaderboardSummary` — `Board`, `Title`, `Periods`, `CurrentPeriodKey`

### LeaderboardController
`src/ROTA.Api/Controllers/LeaderboardController.cs`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /api/leaderboards` | Bearer | Returns discovery list of all boards. |
| `GET /api/leaderboards/{board}?period=&periodKey=&page=` | Bearer | Returns ranked page for board+period. 400 on bad combo or malformed periodKey. |

---

## System 17 — Global Leaderboards (Slice 4)

### ILeaderboardService — write hooks
`src/ROTA.Application/Interfaces/ILeaderboardService.cs`

| Method | Description |
|--------|-------------|
| `Task RecordEnergySpendAsync(playerId, amount, at, ct)` | Increments `EnergySpent/Weekly` and `EnergySpent/Monthly` boards by `amount`. Called by `EnergyService.SpendEnergyAsync` after successful atomic spend, `ResourceType.Energy` only (Q6 — Stamina/GuildStamina excluded). Best-effort: failure swallowed+logged, spend never rolls back. |
| `Task RecordRaidHitAsync(playerId, damageFinal, at, ct)` | Increments `DamageDealt/Weekly` + `DamageDealt/Monthly` by `damageFinal`, and max-updates `LargestHit/Daily` with `damageFinal`. Called by `RaidService.HitRaidAsync` inside the advisory-lock callback immediately after `participant.RecordHit(damageFinal)`. Rides the ambient transaction — atomic with the hit. Never reached on the Redis cached-replay early-return path. |

**Hook placement in EnergyService:**
- File: `src/ROTA.Application/Services/EnergyService.cs`
- Location: `SpendEnergyAsync`, after `await _auditLog.AppendAsync(...)`, inside `if (success)` block, guarded by `if (type == ResourceType.Energy)`
- Adjacent code: `_logger.LogWarning(ex, "Leaderboard write failed …")` in the catch block
- Transaction context: NO ambient tx at this point (atomic spend is a self-contained FOR UPDATE, already committed)
- Failure discipline: try/catch swallows any exception from `RecordEnergySpendAsync`, mirroring `AuditLogMiddleware`

**Hook placement in RaidService:**
- File: `src/ROTA.Application/Services/RaidService.cs`
- Location: `HitRaidAsync`, inside `AtomicApplyHitAsync` callback, immediately after `participantFinal!.RecordHit(damageFinal)`
- Adjacent code: `await _leaderboards.RecordRaidHitAsync(playerId, damageFinal, DateTimeOffset.UtcNow, ct);`
- Transaction context: INSIDE the advisory-lock transaction — ambient `_db.Database.CurrentTransaction` is picked up by `LeaderboardEntryRepository.IncrementAsync/MaxUpdateAsync`, so the board increments are atomic with the hit commit
- Idempotency: the Redis idempotency early-return (step 4 in `HitRaidAsync`) fires BEFORE `AtomicApplyHitAsync` is entered, so the hook is never reached on a cached-replay duplicate

---

## System 17 — Global Leaderboards (Slice 5)

### ILeaderboardEntryRepository — Stat snapshot methods
`src/ROTA.Application/Interfaces/ILeaderboardEntryRepository.cs`

| Method | Description |
|--------|-------------|
| `Task SetValueAsync(playerId, board, period, periodKey, value, at, ct)` | Overwrite-upsert. Inserts on first call; on conflict overwrites Value unconditionally (snapshot semantics). `last_progress_at` bumped to `at` ONLY when the value changed — preserves earliest-to-reach tiebreak across repeated snapshots of the same score. SQL: `INSERT … ON CONFLICT DO UPDATE SET value = EXCLUDED.value, last_progress_at = CASE WHEN leaderboard_entry.value <> EXCLUDED.value THEN @at ELSE leaderboard_entry.last_progress_at END`. Picks up ambient transaction via `_db.Database.CurrentTransaction`. |
| `Task<IReadOnlyList<EligibleStatSnapshot>> GetEligibleStatSnapshotAsync(minLevel, excludeAdmins, ct)` | Single SQL projection: JOIN players + player_stats, applies full eligibility predicate (not banned, not deleted, level >= minLevel, Admin role bit excluded when excludeAdmins=true). Returns `{PlayerId, BaseAttack, BaseDefense, DiscernmentInvestment}` per eligible player. |

**`EligibleStatSnapshot`** (record in `ILeaderboardEntryRepository.cs`):
- `PlayerId Guid`
- `BaseAttack int` — raw stored; maps to StatAttack board
- `BaseDefense int` — raw stored; maps to StatDefense board
- `DiscernmentInvestment int` — raw stored; maps to StatDiscernment board

### ILeaderboardService — snapshot method
`src/ROTA.Application/Interfaces/ILeaderboardService.cs`

| Method | Description |
|--------|-------------|
| `Task<int> SnapshotStatBoardAsync(ct)` | Queries eligible players via `GetEligibleStatSnapshotAsync`, then calls `SetValueAsync` × 3 per player (StatAttack=BaseAttack, StatDefense=BaseDefense, StatDiscernment=DiscernmentInvestment; Period=Live, period_key="live"). Idempotent. Newly-ineligible players' stale rows filtered by the read-path join — no purge needed. Returns count of eligible players snapshotted. |

### AdminController — Stat refresh endpoint
`src/ROTA.Api/Controllers/AdminController.cs`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `POST /api/admin/leaderboards/stat/refresh` | `[AdminOnly]` | DB actor re-verify (FindByIdAsync + HasRole(Admin)); calls `SnapshotStatBoardAsync`; writes `AuditLog(action="StatBoardRefreshed", resultSummary includes actorId + count + timestamp)`. Returns `200 StatBoardRefreshResponse{PlayersSnapshotted, SnapshotAt}`. Non-admin actor → 403. |

**`StatBoardRefreshResponse`** (`src/ROTA.Shared/DTOs/AdminDTOs.cs`):
- `int PlayersSnapshotted` — count of eligible players whose Stat board rows were upserted
- `DateTimeOffset SnapshotAt` — UTC timestamp of the snapshot

### CLI command
`src/ROTA.Api/AdminCli.cs`

| Command | Description |
|---------|-------------|
| `leaderboard-refresh-stat` | Resolves `ILeaderboardService` from DI, calls `SnapshotStatBoardAsync()`, prints count. No DB actor check (CLI/system bypass). |
| `grant-gear` | Grant gear to a player by username or GUID: `grant-gear <user\|guid> <gearDefId> [qty]`. |

**Stat board metric mapping (locked):**
- `StatAttack` board → `PlayerStats.BaseAttack` (raw stored, includes SkillPoint investments)
- `StatDefense` board → `PlayerStats.BaseDefense` (raw stored)
- `StatDiscernment` board → `PlayerStats.DiscernmentInvestment` (raw stored)
- Effective combat power (gear/legion multipliers) is PHASE-2 for this board.

---

## Interfaces

### IAuthService
`src/ROTA.Application/Interfaces/IAuthService.cs`

| Method | Description |
|--------|-------------|
| `Task<AuthResponse?> RegisterAsync(RegisterRequest, string ipAddress)` | Register new player |
| `Task<AuthResponse?> LoginAsync(LoginRequest, string ipAddress)` | Authenticate a player |
| `Task<AuthResponse?> RefreshAsync(RefreshRequest, string ipAddress)` | Rotate refresh token |
| `Task LogoutAsync(RefreshRequest)` | Revoke refresh token |

---

### IEnergyService
`src/ROTA.Application/Interfaces/IEnergyService.cs`

| Method | Description |
|--------|-------------|
| `Task<int> GetCurrentEnergyAsync(Guid playerId, ResourceType, CancellationToken)` | Live value from checkpoint |
| `Task<bool> SpendEnergyAsync(Guid playerId, ResourceType, int amount, CancellationToken)` | Deduct with row lock (participates in ambient tx when inside advisory-lock callback) |
| `Task RefillEnergyAsync(Guid playerId, ResourceType, int amount, CancellationToken)` | Add up to max |
| `Task UpdateMaxAsync(Guid playerId, ResourceType, int newMax, CancellationToken)` | Update pool max value |
| `double GetRegenMinutesPerPoint(PlayerClass, ResourceType)` | Class-based regen rate (minutes/point) from ClassConfig; pure, no DB. Backs the profile DTO's `RegenMinutesPerPoint`. |

---

### IGemService
`src/ROTA.Application/Interfaces/IGemService.cs`

| Method | Description |
|--------|-------------|
| `Task<int> GetBalanceAsync(Guid playerId, CancellationToken)` | Balance from ledger sum |
| `Task<bool> GrantGemsAsync(Guid, int, GemTransactionType, string? referenceId, CancellationToken)` | Credit gems idempotently |
| `Task<GemSpendOutcome> SpendGemsAsync(Guid, int, GemTransactionType, string? referenceId, CancellationToken)` | Debit gems; tri-state: `Charged` / `AlreadyProcessed` (refId already in ledger → idempotent replay, caller re-runs grant) / `InsufficientBalance`. Closes the lost-purchase hole across all 3 shops. |
| `Task<bool> DailyRefillAsync(Guid playerId, CancellationToken)` | Once-per-day 5 gems |

---

### IPlayerService
`src/ROTA.Application/Interfaces/IPlayerService.cs`

| Method | Description |
|--------|-------------|
| `Task<PlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken)` | Full profile (incl. DisplayName + Class), live values. Each resource carries class-based `RegenMinutesPerPoint` (double) + `SecondsToNextPoint` (int) for client refill timers; legacy `RegenPerMinute` (int) is vestigial. |
| `Task<UpdateUsernameResult> UpdateUsernameAsync(Guid, UpdateUsernameRequest, CancellationToken)` | Username update |
| `Task<UpdateDisplayNameResult> UpdateDisplayNameAsync(Guid, UpdateDisplayNameRequest, CancellationToken)` | Change player's DisplayName; audited |

---

### IStatService
`src/ROTA.Application/Interfaces/IStatService.cs`

| Method | Description |
|--------|-------------|
| `Task<AllocateStatResponse> AllocateStatPointAsync(Guid, StatType, int, CancellationToken)` | Invest SkillPoints in stat |
| `Task GrantLevelUpPointsAsync(Guid playerId, int newLevel, CancellationToken)` | +10 SP +5 gems at L%5 |
| `Task AddUnassignedPointsAsync(Guid playerId, int amount, CancellationToken)` | Grant SP no LSI check |
| `Task<PlayerStatsResponse?> GetStatsAsync(Guid playerId, CancellationToken)` | Full stat sheet |
| `int XpToNextLevel(int level)` | XP needed for next level |
| `CritProfile GetCritProfile(int discernmentInvestment)` | Crit chance + multiplier for given discernment |

---

### IQuestService
`src/ROTA.Application/Interfaces/IQuestService.cs`

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<QuestAvailabilityResponse>> GetAvailableQuestsAsync(Guid, CancellationToken)` | Filtered quest list |
| `Task<QuestResultResponse> AttemptQuestAsync(Guid, string questId, QuestDifficulty, CancellationToken)` | Attempt quest |

---

### IRaidService
`src/ROTA.Application/Interfaces/IRaidService.cs`

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(Guid, CancellationToken)` | Active raids list |
| `Task<IReadOnlyList<CompletedRaidResponse>> GetCompletedRaidsAsync(Guid, CancellationToken)` | Caller's completed raids with persisted reward summary; limit 50, newest first |
| `Task<SummonRaidResult> SummonRaidAsync(Guid, string raidDefinitionId, RaidDifficulty, CancellationToken)` | Summon raid |
| `Task<RaidHitResult> HitRaidAsync(Guid, Guid activeRaidId, int hitSize, string key, CancellationToken)` | Hit a raid |
| `Task<IReadOnlyList<RaidParticipantRankDto>> GetParticipantsAsync(Guid activeRaidId, int top, CancellationToken)` | Ranked participants by total damage (desc); `top` clamped to 1..100 |

---

### IItemService
`src/ROTA.Application/Interfaces/IItemService.cs`

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<InventoryItemResponse>> GetInventoryAsync(Guid, CancellationToken)` | Player inventory |
| `Task<UseItemResponse> UseItemAsync(Guid, string itemDefinitionId, int quantity, CancellationToken)` | Use item |

---

### IClassService
`src/ROTA.Application/Interfaces/IClassService.cs`

| Method | Description |
|--------|-------------|
| `ClassRegenRates GetRegenRates(PlayerClass)` | Regen rates for class |
| `IReadOnlyList<PlayerClass> GetAvailableChoices(int level, PlayerClass)` | Valid class choices |
| `Task<PlayerClass> AssignClassAsync(Guid, PlayerClass, CancellationToken)` | Assign chosen class |
| `PlayerClass ComputeAutoAdvance(int level, PlayerClass)` | Auto-advance check |
| `bool IsConvergedClass(PlayerClass)` | True if Luminary+ |

---

### IEquipmentService
`src/ROTA.Application/Interfaces/IEquipmentService.cs`

| Method | Description |
|--------|-------------|
| `Task<EquipResult> EquipAsync(Guid, string slotName, string gearDefinitionId, CancellationToken)` | Equip or swap gear in a slot |
| `Task<UnequipResult> UnequipAsync(Guid, string slotName, CancellationToken)` | Remove gear from a slot |
| `Task<IReadOnlyList<EquippedItemResponse>> GetEquipmentAsync(Guid, CancellationToken)` | All equipped items |
| `Task<IReadOnlyList<OwnedGearResponse>> GetOwnedGearAsync(Guid, CancellationToken)` | Owned gear bag — qty per def with equipped + available counts (System 18) |
| `Task<EffectiveCombatData> GetEffectiveCombatDataAsync(Guid, int baseAtk, int baseDef, CancellationToken)` | Effective stats + proc + conditional bonuses for combat |
| `Task GrantGearAsync(Guid playerId, string gearDefinitionId, int quantity, ct)` | Idempotent upsert: stacks onto existing PlayerGear row or creates one. Safe for loot distribution. |

**Records (same file):**

```csharp
record EffectiveCombatData(int EffectiveAttack, int EffectiveDefense, GearProcData? MountProc, double FlatDamagePercent)
record GearProcData(double ProcChance, double ProcPercent)
```

---

### IAdminService
`src/ROTA.Application/Interfaces/IAdminService.cs`

| Method | Description |
|--------|-------------|
| `Task<AdminActionResult> GrantRoleAsync(Guid actorId, string targetUsernameOrId, PlayerRoles role, CancellationToken)` | Grant role — DB actor re-verify, Guid.Empty skips for CLI |
| `Task<AdminActionResult> RevokeRoleAsync(Guid actorId, string targetUsernameOrId, PlayerRoles role, CancellationToken)` | Revoke role — last-admin guard, revokes sessions |

---

### IBetaKeyService
`src/ROTA.Application/Interfaces/IBetaKeyService.cs`

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<BetaKey>> GenerateAsync(Guid? actorPlayerId, int count, CancellationToken)` | Generate 1–100 ROTA-XXXX-XXXX-XXXX keys; audits BetaKeyGenerated |
| `Task<bool> ValidateAndRedeemAsync(string key, Guid newPlayerId, CancellationToken)` | Atomically redeem key via TryRedeemAsync |

---

### Repository Interfaces
`src/ROTA.Application/Interfaces/`

**IPlayerRepository**

| Method | Description |
|--------|-------------|
| `Task<Player?> FindByIdAsync(Guid, CancellationToken)` | Find player by PK |
| `Task<Player?> FindByEmailAsync(string, CancellationToken)` | Find by email |
| `Task<bool> EmailExistsAsync(string, CancellationToken)` | Email uniqueness check |
| `Task<bool> UsernameExistsAsync(string, CancellationToken)` | Username uniqueness check |
| `Task<Player> CreateAsync(Player, CancellationToken)` | Persist new player |
| `Task<Player?> FindByIdWithResourcesAsync(Guid, CancellationToken)` | With Resources eager |
| `Task<Player?> FindByIdWithStatsAsync(Guid, CancellationToken)` | With Stats eager |
| `Task UpdateAsync(Player, CancellationToken)` | Persist player changes |
| `Task UpdateStatsAsync(PlayerStats, CancellationToken)` | Persist stats changes |
| `Task<Player?> FindByUsernameAsync(string, CancellationToken)` | Find by username |
| `Task<int> CountByRoleAsync(PlayerRoles, CancellationToken)` | Count by bitwise role flag |

**IPlayerResourceRepository** — `GetAsync`, `AtomicUpdateAsync` (row-level FOR UPDATE; detects ambient transaction)

**IAuditLogRepository** — `AppendAsync(AuditLog, CancellationToken)` (append-only)

**IRefreshTokenRepository** — find/create/revoke refresh tokens; `RevokeAllActiveAsync(Guid, CancellationToken)`

**IBetaKeyRepository**

| Method | Description |
|--------|-------------|
| `Task<BetaKey> CreateAsync(BetaKey, CancellationToken)` | Persist new key |
| `Task<BetaKey?> GetByKeyAsync(string, CancellationToken)` | Find by key string |
| `Task<IReadOnlyList<BetaKey>> ListAsync(int take, CancellationToken)` | List recently created |
| `Task<bool> TryRedeemAsync(string key, Guid playerId, CancellationToken)` | Atomic conditional UPDATE — race guard |
| `Task<T> WithTransactionAsync<T>(Func<CancellationToken, Task<T>>, CancellationToken)` | Wrap work in a DB transaction |

**IGemTransactionRepository** — append gem ledger entries, sum balance

**IQuestProgressRepository** — find/upsert quest completion records

**IQuestDifficultyProgressRepository** — per-difficulty completion counts

**IActiveRaidRepository** — find/create/update active raids; `AtomicApplyHitAsync` (advisory lock + EF tx)

**IRaidParticipantRepository** — find/upsert participant damage records; `GetTopByDamageAsync` (ranked leaderboard, joins players for DisplayName, returns `RaidParticipantRank`)

**IPlayerInventoryRepository** — `GetAllForPlayerAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`

**IPlayerEquipmentRepository** — `FindBySlotAsync`, `GetEquippedAsync`, `CreateAsync`, `UpdateAsync`

**IPlayerGearRepository** — `GetOwnedAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync` (gear ownership stacks; System 18)

---

## Services

### AuthService → IAuthService
`src/ROTA.Application/Services/AuthService.cs`
Implements: register/login/refresh/logout. BCrypt(12) hashing, RS256 JWT. Redis lockout (5 fails → 15 min). Max 3 concurrent sessions — revokes oldest on 4th.

### EnergyService → IEnergyService
`src/ROTA.Application/Services/EnergyService.cs`
Live value = checkpoint + elapsed × regenPerMinute (class-based via ClassConfig), capped at max. SpendEnergy uses FOR UPDATE row lock via `AtomicUpdateAsync` — participates in ambient transaction when called from within `AtomicApplyHitAsync`.

### PlayerService → IPlayerService
`src/ROTA.Application/Services/PlayerService.cs`
Profile with live resource values via IEnergyService. Username update with uniqueness re-check.

### GemService → IGemService
`src/ROTA.Application/Services/GemService.cs`
Balance = SUM(amount) from ledger. Idempotency via unique index on (player_id, type, reference_id).

### StatService → IStatService
`src/ROTA.Application/Services/StatService.cs`
LSI cap 9.0 for Energy/Stamina. XpToNextLevel: `max(floor, round(30 × level^0.7))`.
Constructor: `(IPlayerRepository, IEnergyService, IGemService, IAuditLogRepository, IOptions<LevelingConfig>, IOptions<CombatConfig>, IClassService, IEquipmentService)`

### QuestService → IQuestService
`src/ROTA.Application/Services/QuestService.cs`
Static definitions from content/quests.json. Energy spent first. Level-ups via `player.AddExperience(xp, _stats.XpToNextLevel)`.

### RaidService → IRaidService
`src/ROTA.Application/Services/RaidService.cs`
Server-seeded RNG damage. Redis idempotency (24h TTL). Contribution tiers → reward multipliers. Level-ups same pattern as QuestService. Stamina spend inside advisory-lock tx (atomic with hit).
Damage pipeline (Slice 5 final): `base=(ATK×4+DEF)×hitSize×RNG[0.85,1.15]` → `preProc=base` → mount proc (`preProc×ProcPercent`) → magic DamageProcs (each: roll `procChance`, accumulate `procAmount×preProc`, cap total at `MaxAggregateProcBonus×preProc`) → magic CritChanceFlat (always-on sum added to disc crit chance, clamped at 1.0) → crit → FlatDamagePercent → `TakeDamage`. GoldProc/XpProc applied after base xp/gold computed, before `AddExperience`/`AddGold`. Stacks=false respected per effectType.
New injected deps (Slices 4–5): `IRaidMagicRepository`, `IMagicDefinitionProvider`, `IOptions<MagicConfig>`.
`RaidHitResponse` gains: `long MagicProcBonus`, `List<MagicProcDTO> MagicProcs` ({Name,Bonus}), `double MagicCritBonus`.

### ItemService → IItemService
`src/ROTA.Application/Services/ItemService.cs`
StatBag: unassigned SkillPoints. Sigil: summon raid + consume item.

### ClassService → IClassService
`src/ROTA.Application/Services/ClassService.cs`
Path tiers L5-1000. Convergence L2000+. Strip Legendary/Ascendant prefix for regen lookup.

### EquipmentService → IEquipmentService
`src/ROTA.Application/Services/EquipmentService.cs`
Equip/unequip/list gear. `EquipAsync` is ownership-gated (System 18 G3): requires owned_qty − equipped_count ≥ 1. `GetOwnedGearAsync`: owned gear stacks hydrated with definitions; `Available = Owned − Equipped` (equipped count derived from `PlayerEquipment`; ownership is permanent, never consumed by equip/unequip). `GetEffectiveCombatDataAsync`: sums base gear stats, evaluates all `ConditionalBonuses` from equipped gear against player inventory (per-hit, indexed), folds results into effective ATK/DEF/proc/FlatDamagePercent. ProcChanceFlat clamped to 1.0 after accumulation.
Constructor: `(IPlayerEquipmentRepository, IGearDefinitionProvider, IAuditLogRepository, IPlayerInventoryRepository, IItemDefinitionProvider, IPlayerGearRepository)`

### IMagicService
`src/ROTA.Application/Interfaces/IMagicService.cs`

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<OwnedMagicResponse>> GetOwnedMagicsAsync(Guid playerId, ct)` | Owned magics hydrated with definitions |
| `Task<MagicApplyResult> ApplyMagicAsync(Guid playerId, Guid raidId, string defId, bool isAdmin, ct)` | Apply magic to raid; enforces slot cap inside advisory lock, world gate, one-per-player |
| `Task<MagicApplyResult> RemoveMagicAsync(Guid playerId, Guid raidId, string defId, bool isAdmin, ct)` | Soft-delete; world=admin only, non-world=summoner only |
| `Task GrantMagicAsync(Guid playerId, string defId, ct)` | Idempotent upsert; safe to call from reward distribution (duplicate = no-op in repo) |
| `Task<BuyMagicResult> BuyMagicAsync(Guid playerId, string defId, ct)` | Spend GemPrice gems, then GrantMagicAsync; duplicate purchase is allowed (charges again, grant is no-op) |

Validation order (Apply): raid active → world gate → owns magic → participant (non-world) → one-per-player (non-world) → [advisory lock] → duplicate check → slot cap → insert.
Economy: `MagicDefinition.GemPrice = 0` = not for sale; magics.json sets gemPrice per magic. `GemTransactionType.MagicPurchase = 7` added.
LootTable: `ThresholdReward.MagicDrops` (raid) + `LootTableDifficulty.MagicDrops` (quest) are `List<MagicDropChance>` ({MagicId, Chance}). `GrantMagicAsync` called per qualifying drop.

Implementation: `MagicService` (`src/ROTA.Application/Services/MagicService.cs`) — constructor now includes `IGemService`.

---

### IRaidMagicRepository
`src/ROTA.Application/Interfaces/IRaidMagicRepository.cs`

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<RaidMagic>> GetForRaidAsync(Guid activeRaidId, ct)` | Non-deleted magics on a raid (≤5 rows in combat) |
| `Task<int> CountForRaidAsync(Guid activeRaidId, ct)` | Count for slot-cap check (called inside advisory lock) |
| `Task<RaidMagic?> FindAsync(Guid activeRaidId, string defId, ct)` | Non-deleted row; duplicate-check inside advisory lock |
| `Task<RaidMagic?> FindByPlayerAsync(Guid activeRaidId, Guid playerId, ct)` | One-per-player pre-check |
| `Task<RaidMagic> CreateAsync(RaidMagic, ct)` | Insert inside advisory-lock tx |
| `Task SoftDeleteAsync(RaidMagic, ct)` | Soft-delete on remove |

Implementation: `RaidMagicRepository` (`src/ROTA.Infrastructure/Persistence/Repositories/RaidMagicRepository.cs`)

---

### IPlayerMagicRepository
`src/ROTA.Application/Interfaces/IPlayerMagicRepository.cs`

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<PlayerMagic>> GetOwnedAsync(Guid playerId, ct)` | Non-deleted ownership rows |
| `Task<PlayerMagic?> FindAsync(Guid playerId, string defId, ct)` | Any row regardless of IsDeleted (for upsert check) |
| `Task UpsertAsync(Guid playerId, string defId, ct)` | Idempotent grant: creates or restores |

Implementation: `PlayerMagicRepository` (`src/ROTA.Infrastructure/Persistence/Repositories/PlayerMagicRepository.cs`)

---

### IMagicDefinitionProvider
`src/ROTA.Application/Interfaces/IMagicDefinitionProvider.cs`
Singleton; reads `content/magics.json` at startup. Throws on duplicate id, procChance outside [0,1], or negative procAmount.

| Method | Description |
|--------|-------------|
| `MagicDefinition? GetById(string id)` | Look up by id; null if not found |
| `IReadOnlyList<MagicDefinition> GetAll()` | All 10 starter magics |

Implementation: `MagicDefinitionProvider` (`src/ROTA.Infrastructure/Services/MagicDefinitionProvider.cs`)

---

### ConditionalBonusEvaluator (static)
`src/ROTA.Application/Services/ConditionalBonusEvaluator.cs`
Shared evaluator for gear and future troops/legions. `Evaluate(bonuses, ownedById, ownedByTag, equippedSlots) → AccumulatedBonuses`. Returns raw sums; callers apply clamping.

### AdminService → IAdminService
`src/ROTA.Application/Services/AdminService.cs`
GrantRoleAsync: DB actor re-verify (skip Guid.Empty CLI), resolve target by GUID or username, guard base Player role, audit RoleGranted.
RevokeRoleAsync: same actor verify, cannot revoke Player, last-admin guard (CountByRoleAsync <= 1 → fail), RevokeAllActiveAsync on Admin/Moderator removal, audit RoleRevoked.

### BetaKeyService → IBetaKeyService
`src/ROTA.Application/Services/BetaKeyService.cs`
ROTA-XXXX-XXXX-XXXX Crockford base32 keygen via RandomNumberGenerator. GenerateAsync: creates + persists N keys, audits BetaKeyGenerated (Guid.Empty actor → null CreatedByPlayerId).

### SeedData (static)
`src/ROTA.Infrastructure/Seeding/SeedData.cs`
EnsureAdminAsync: idempotent bootstrap. Reads Seed:AdminPassword (required, no default) and Seed:AdminEmail (default xolaces@rota.dev). Creates "Xolaces" with Player|Admin roles and DisplayName="DEV_Xolaces". BCrypt(12).

---

## Controllers

### AuthController — `api/auth`
`src/ROTA.Api/Controllers/AuthController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `POST /api/auth/register` | `RegisterAsync` | 201, 400, 409 |
| `POST /api/auth/login` | `LoginAsync` | 200, 400, 401 |
| `POST /api/auth/refresh` | `RefreshAsync` | 200, 400, 401 |
| `POST /api/auth/logout` [Auth] | `LogoutAsync` | 204, 400, 401 |

### PlayerController — `api/players` [Authorize]
`src/ROTA.Api/Controllers/PlayerController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/players/me` | `GetProfileAsync` | 200, 404 |
| `PUT /api/players/me` | `UpdateUsernameAsync` | 200, 400, 404, 409 |
| `PUT /api/players/me/display-name` | `UpdateDisplayNameAsync` | 200, 400, 404 |

### QuestController — `api/quests` [Authorize]
`src/ROTA.Api/Controllers/QuestController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/quests` | `GetAvailableQuestsAsync` | 200 |
| `POST /api/quests/{questId}/attempt` | `AttemptQuestAsync` | 200, 400, 403, 404, 422 |

### RaidController — `api/raids` [Authorize]
`src/ROTA.Api/Controllers/RaidController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/raids` | `GetActiveRaidsAsync` | 200 |
| `GET /api/raids/completed` | `GetCompletedRaidsAsync` | 200 — caller's defeated raids with reward summary; newest first, limit 50 |
| `POST /api/raids/{raidDefinitionId}/summon` | `SummonRaidAsync` | 201, 400, 404, 422 |
| `POST /api/raids/{activeRaidId}/hit` | `HitRaidAsync` | 200, 400, 404, 409, 410, 422 |
| `GET /api/raids/{activeRaidId}/participants?top=` | `GetParticipantsAsync` | 200 ranked participant list |

### ItemController — `api/items` [Authorize]
`src/ROTA.Api/Controllers/ItemController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/items` | `GetInventoryAsync` | 200 |
| `POST /api/items/{itemDefinitionId}/use` | `UseItemAsync` | 200, 404, 422 |

### StatController — `api/stats` [Authorize]
`src/ROTA.Api/Controllers/StatController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/stats/me` | `GetStatsAsync` | 200, 404 |
| `POST /api/stats/allocate` | `AllocateStatPointAsync` | 200, 400, 422 |
| `GET /api/stats/class` | `GetClassInfoAsync` | 200, 404 |
| `POST /api/stats/class/choose` | `AssignClassAsync` | 200, 400, 403 |

### MagicController — `api/magics` + `api/raids/{id}/magics` [Authorize]
`src/ROTA.Api/Controllers/MagicController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/magics` | `GetOwnedMagicsAsync` | 200 |
| `POST /api/raids/{raidId}/magics` | `ApplyMagicAsync` | 200, 400, 403, 404, 409, 422 |
| `DELETE /api/raids/{raidId}/magics/{magicDefinitionId}` | `RemoveMagicAsync` | 200, 403, 404 |

---

### EquipmentController — `api/equipment` [Authorize]
`src/ROTA.Api/Controllers/EquipmentController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/equipment` | `GetEquipmentAsync` | 200 |
| `GET /api/equipment/owned` | `GetOwnedGearAsync` | 200 |
| `PUT /api/equipment/{slot}` | `EquipAsync` | 200, 400, 404 |
| `DELETE /api/equipment/{slot}` | `UnequipAsync` | 200, 400, 404 |

### AdminController — `api/admin` [AdminOnly]
`src/ROTA.Api/Controllers/AdminController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `POST /api/admin/players/{idOrUsername}/roles/grant` | `GrantRoleAsync` | 200, 400, 403, 404 |
| `POST /api/admin/players/{idOrUsername}/roles/revoke` | `RevokeRoleAsync` | 200, 400, 403, 404 |
| `POST /api/admin/beta-keys` | `GenerateAsync` | 200 `{ keys: [...] }` |
| `GET /api/admin/beta-keys` | `ListAsync` | 200 list with redeemed status |

---

## Entities

### Player
`src/ROTA.Domain/Entities/Player.cs`

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `Username` | `string` |
| `Email` | `string` |
| `PasswordHash` | `string` |
| `Level` | `int` |
| `Experience` | `long` (XP toward next level, not cumulative) |
| `Gold` | `long` |
| `Roles` | `PlayerRoles` (bitwise flags, default Player) |
| `DisplayName` | `string` (max 48) |
| `Class` | `PlayerClass` |
| `GuildId` | `Guid?` |
| `GuildRank` | `string?` |
| `IsBanned` | `bool` |
| `BanReason` | `string?` |
| `CreatedAt` | `DateTimeOffset` |
| `UpdatedAt` | `DateTimeOffset` |
| `IsDeleted` | `bool` |
| `Stats` | `PlayerStats?` (nav) |
| `Resources` | `ICollection<PlayerResource>` (nav) |

Domain methods:

| Method | Description |
|--------|-------------|
| `Create(username, email, passwordHash)` | Factory, seeds Stats+Resources |
| `CreateWithId(Guid id, username, email, passwordHash)` | Factory with pre-allocated ID (beta gate) |
| `IReadOnlyList<int> AddExperience(long, Func<int,int> xpToNextLevel)` | XP carry-over, returns new levels |
| `void AddGold(long)` | Increase gold balance |
| `void UpdateUsername(string)` | Change username |
| `void UpdateDisplayName(string)` | Change display name (max 48) |
| `void GrantRole(PlayerRoles)` | Add role flag, bumps UpdatedAt |
| `void RevokeRole(PlayerRoles)` | Remove role flag; Player flag is permanent |
| `bool HasRole(PlayerRoles)` | Bitwise check |
| `void Ban(string reason)` | Set banned flag |
| `void SoftDelete()` | Mark deleted |
| `void SetClass(PlayerClass)` | Assign class tier |

---

### PlayerStats
`src/ROTA.Domain/Entities/PlayerStats.cs`

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `PlayerId` | `Guid` |
| `BaseAttack` | `int` |
| `BaseDefense` | `int` |
| `BaseMaxHealth` | `int` |
| `CurrentHealth` | `int` |
| `EnergyInvestment` | `int` |
| `StaminaInvestment` | `int` |
| `DiscernmentInvestment` | `int` |
| `SkillPoints` | `int` |
| `UpdatedAt` | `DateTimeOffset` |

Domain methods:

| Method | Description |
|--------|-------------|
| `Create(Guid playerId)` | Factory with zero investments |
| `int ComputeMaxEnergy()` | 10 + EnergyInvestment |
| `int ComputeMaxStamina()` | 10 + StaminaInvestment |
| `double ComputeLSI(int level)` | (Energy + Stamina×2) / level |
| `void AddSkillPoints(int)` | Grant SP |
| `void AllocateToEnergy(int)` | Invest SP to energy |
| `void AllocateToStamina(int)` | Invest SP to stamina |
| `void AllocateToDiscernment(int)` | Invest SP to discernment |
| `void AllocateToAttack(int)` | Invest SP to attack |
| `void AllocateToDefense(int)` | Invest SP to defense |
| `void AllocateToHealth(int)` | Invest SP to health |

---

### PlayerResource
`src/ROTA.Domain/Entities/PlayerResource.cs`

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `PlayerId` | `Guid` |
| `ResourceType` | `ResourceType` |
| `CurrentValue` | `int` (checkpoint only) |
| `MaxValue` | `int` |
| `RegenPerMinute` | `int` |
| `LastRegenAt` | `DateTimeOffset` |
| `UpdatedAt` | `DateTimeOffset` |

Domain methods: `Create(...)`, `SaveCheckpoint(int, DateTimeOffset)`, `SetMaxValue(int, DateTimeOffset)`

---

### PlayerEquipment
`src/ROTA.Domain/Entities/PlayerEquipment.cs`

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `PlayerId` | `Guid` |
| `Slot` | `EquipmentSlot` |
| `GearDefinitionId` | `string` |
| `EquippedAt` | `DateTimeOffset` |
| `IsDeleted` | `bool` |

Domain methods: `Create(Guid, EquipmentSlot, string)`, `Equip(string)`, `Unequip()`

### PlayerGear
`src/ROTA.Domain/Entities/PlayerGear.cs` — System 18 gear ownership (stacks).

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `PlayerId` | `Guid` |
| `GearDefinitionId` | `string` |
| `Quantity` | `int` |
| `IsDeleted` | `bool` |

Domain methods: `Create(Guid, string, int=1)`, `AddQuantity(int)`. One row per `(player_id, gear_definition_id)`; ownership is permanent (equip/unequip never changes quantity). Table `player_gear` (migration `AddPlayerGear`).

---

### ActiveRaid
`src/ROTA.Domain/Entities/ActiveRaid.cs`

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `RaidDefinitionId` | `string` |
| `SummonedByPlayerId` | `Guid` |
| `CurrentHp` | `long` |
| `MaxHp` | `long` |
| `IsDefeated` | `bool` |
| `Difficulty` | `RaidDifficulty` |
| `ExpiresAt` | `DateTimeOffset` |
| `ParticipantCount` | `int` (denormalized) |
| `CreatedAt` | `DateTimeOffset` |
| `UpdatedAt` | `DateTimeOffset` |
| `IsDeleted` | `bool` |

Domain methods: `Create(...)`, `TakeDamage(long)`, `MarkDefeated()`, `IncrementParticipantCount()`

---

### BetaKey
`src/ROTA.Domain/Entities/BetaKey.cs`

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `Key` | `string` (ROTA-XXXX-XXXX-XXXX) |
| `CreatedByPlayerId` | `Guid?` (null for CLI/system) |
| `IsRedeemed` | `bool` |
| `RedeemedByPlayerId` | `Guid?` |
| `RedeemedAt` | `DateTimeOffset?` |
| `CreatedAt` | `DateTimeOffset` |
| `UpdatedAt` | `DateTimeOffset` |
| `IsDeleted` | `bool` |

Domain methods: `Create(string key, Guid? createdBy)` factory; `Redeem(Guid playerId)`

### RaidMagic
`src/ROTA.Domain/Entities/RaidMagic.cs`

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `ActiveRaidId` | `Guid` |
| `MagicDefinitionId` | `string` |
| `AppliedByPlayerId` | `Guid` |
| `CreatedAt` | `DateTimeOffset` |
| `UpdatedAt` | `DateTimeOffset` |
| `IsDeleted` | `bool` |

Domain methods: `Create(Guid activeRaidId, string defId, Guid appliedByPlayerId)` factory; `SoftDelete()`.
Unique index on `(active_raid_id, magic_definition_id)` — no duplicate magic per raid.
Advisory lock on `active_raid_id` guards count→insert atomicity.

---

### PlayerMagic
`src/ROTA.Domain/Entities/PlayerMagic.cs`

| Property | Type |
|----------|------|
| `Id` | `Guid` |
| `PlayerId` | `Guid` |
| `MagicDefinitionId` | `string` |
| `Quantity` | `int` (= 1; reserved for future consumable model) |
| `CreatedAt` | `DateTimeOffset` |
| `UpdatedAt` | `DateTimeOffset` |
| `IsDeleted` | `bool` |

Domain methods: `Create(Guid playerId, string defId)` factory; `Restore()` un-deletes for re-grant.
Unique index on `(player_id, magic_definition_id)`.

---

### Other Entities (summary)

**RefreshToken** — `Id, PlayerId, TokenHash, ExpiresAt, RevokedAt, IpAddress, IsDeleted`

**AuditLog** — `Id, PlayerId, Action, Timestamp, InputHash, ResultSummary, IpAddress`; `AuditLog.Create(...)` factory

**GemTransaction** — `Id, PlayerId, Amount, TransactionType, ReferenceId, CreatedAt`; append-only ledger

**PlayerQuestProgress** — `Id, PlayerId, QuestId, CompletionCount, UpdatedAt`

**PlayerQuestDifficultyProgress** — `Id, PlayerId, QuestId, Difficulty, CompletionCount, UpdatedAt`

**RaidParticipant** — `Id, ActiveRaidId, PlayerId, DamageDealt, UpdatedAt`

**PlayerInventoryItem** — `Id, PlayerId, ItemDefinitionId, Quantity, UpdatedAt`

---

## Models (content definitions)

### GearDefinition
`src/ROTA.Application/Models/GearDefinition.cs`

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `string` | |
| `Name` | `string` | |
| `Description` | `string` | |
| `Rarity` | `ItemRarity` | |
| `Slot` | `string` | parsed to `EquipmentSlot` |
| `BonusAttack` | `int` | flat base stat bonus |
| `BonusDefense` | `int` | flat base stat bonus |
| `ProcChance` | `double?` | null = no proc (Mount slot only) |
| `ProcPercent` | `double?` | bonus = baseDamage × ProcPercent |
| `IconPath` | `string` | |
| `ConditionalBonuses` | `List<ConditionalBonus>` | empty = no conditional bonuses |

### ConditionalBonus
`src/ROTA.Application/Models/ConditionalBonus.cs`

| Property | Type | Notes |
|----------|------|-------|
| `ConditionType` | `ConditionType` | OwnedUnitCount / OwnedTypeCount / EquippedSlot |
| `ConditionTarget` | `string` | item ID, tag string, or slot name |
| `PerCount` | `int` | denominator for floor division (1 = binary) |
| `BonusType` | `BonusType` | FlatAttack / FlatDefense / ProcChanceFlat / ProcAmountFlat / FlatDamagePercent |
| `BonusAmount` | `double` | bonus gained per stack |

Eval rule: `floor(owned / perCount) × bonusAmount`

### MagicDefinition
`src/ROTA.Application/Models/MagicDefinition.cs`

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `string` | |
| `Name` | `string` | |
| `Description` | `string` | |
| `Rarity` | `ItemRarity` | |
| `Category` | `MagicCategory` | Damage/Crit/Gold/Leveling/Utility (UI-only) |
| `EffectType` | `MagicEffectType` | DamageProc/CritChanceFlat/GoldProc/XpProc |
| `ProcChance` | `double` | 0..1; ignored for CritChanceFlat (always-on) |
| `ProcAmount` | `double` | fraction/multiplier per EffectType |
| `Conditions` | `List<ConditionalBonus>` | ownership scaling (empty in starter content) |
| `Stacks` | `bool` | whether effect stacks with same-EffectType magics |
| `IconPath` | `string` | |
| `Acquisition` | `string` | informational |

### ItemDefinition
`src/ROTA.Application/Models/ItemDefinition.cs`

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `string` | |
| `Name` | `string` | |
| `Description` | `string` | |
| `Rarity` | `ItemRarity` | |
| `Type` | `ItemType` | |
| `ArtKey` | `string` | |
| `StatPointsOnUse` | `int` | StatBag only |
| `IsCraftingIngredient` | `bool` | |
| `SummonRaidId` | `string?` | Sigil only |
| `SummonDifficulty` | `string?` | Sigil only |
| `SummonSize` | `string?` | Sigil only; null → Personal |
| `Tags` | `List<string>` | used by OwnedTypeCount conditional bonus lookups |

---

## Enums

### PlayerClass (`src/ROTA.Domain/Enums/PlayerClass.cs`)
| Value | Tier | Notes |
|-------|------|-------|
| `Conscript = 0` | 1 | Default (L1-4) |
| `Ironguard = 1`, `Arcanist = 2`, `Sentinel = 3` | 2 | Chosen at L5 |
| `Stormguard=10`, `Bloodguard=11`, `Siegebreaker=12` | 3 | Ironguard path (L100) |
| `HighArcanist=20`, `ShadowArcanist=21`, `Runecaller=22` | 3 | Arcanist path (L100) |
| `IroncladSentinel=30`, `Voidwalker=31`, `Dawnblade=32` | 3 | Sentinel path (L100) |
| `LegendaryIronguard=101` … `LegendaryDawnblade=132` | 4 | Auto at L500 |
| `AscendantIronguard=201` … `AscendantDawnblade=232` | 5 | Auto at L1000 |
| `Luminary=300` | 6 | Convergence at L2000 — all paths merge |
| `Immortal=400` | 7 | Auto at L5000 |
| `Archon=500` | 8 | Auto at L7500 |
| `Ancient=600` | 9 | Auto at L10000 |
| `ElderAncient=700` | 10 | Auto at L15000 |
| `Eternal=800` | 11 | Auto at L25000 |

### PlayerRoles (`src/ROTA.Domain/Enums/PlayerRoles.cs`)
`[Flags]` — stored as a single int column; bitwise OR for multiple roles

| Value | Int | Notes |
|-------|-----|-------|
| `None = 0` | 0 | Never assigned in practice |
| `Player = 1` | 1 | All registered accounts — permanent, cannot be revoked |
| `Moderator = 2` | 2 | Mod tooling access |
| `Admin = 4` | 4 | Full access; last-admin protection enforced |

### ConditionType (`src/ROTA.Application/Models/ConditionalBonus.cs`)
| Value | Behavior |
|-------|----------|
| `OwnedUnitCount` | `floor(qty(conditionTarget) / perCount)` stacks |
| `OwnedTypeCount` | `floor(totalQty(tag=conditionTarget) / perCount)` stacks |
| `EquippedSlot` | 1 stack if slot occupied, 0 otherwise |

### BonusType (`src/ROTA.Application/Models/ConditionalBonus.cs`)
| Value | Effect |
|-------|--------|
| `FlatAttack` | folded into `EffectiveCombatData.EffectiveAttack` |
| `FlatDefense` | folded into `EffectiveCombatData.EffectiveDefense` |
| `ProcChanceFlat` | added to `MountProc.ProcChance`; clamped to 1.0 |
| `ProcAmountFlat` | added to `MountProc.ProcPercent` |
| `FlatDamagePercent` | `damageFinal *= (1 + total)` after crit |

### EquipmentSlot (`src/ROTA.Domain/Enums/EquipmentSlot.cs`)
`Head, Neck, Torso, Ring1, Ring2, Mount, Boots, Gloves`

### MagicCategory (`src/ROTA.Domain/Enums/MagicCategory.cs`)
`Damage, Crit, Gold, Leveling, Utility` — informational/UI only; no combat logic reads this.

### MagicEffectType (`src/ROTA.Domain/Enums/MagicEffectType.cs`)
| Value | Behavior |
|-------|----------|
| `DamageProc` | Roll `procChance`; on success add `procAmount × base` damage |
| `CritChanceFlat` | Always-on; add `procAmount` to crit chance before roll |
| `GoldProc` | Roll `procChance`; on success add `procAmount × goldGained` |
| `XpProc` | Roll `procChance`; on success multiply `xpGained` by `procAmount` |

### Other Enums
- **ResourceType** — `Energy, Stamina, GuildStamina`
- **StatType** — `Energy, Stamina, Discernment, Attack, Defense, Health`
- **GemTransactionType** — `DailyRefill, QuestReward, RaidReward, LevelUpReward, Purchase, Spend`
- **QuestDifficulty** — `Normal, Hard, Legendary, Nightmare`
- **RaidDifficulty** — `Normal, Hard, Legendary, Nightmare`
- **RaidSize** — `Personal=0, Small=1, Medium=2, Large=3, Titanic=4`
- **ItemType** — `Equipment, Material, StatBag, Sigil, Consumable`
- **ItemRarity** — `Grey=0, White=1, Green=2, Blue=3, Purple=4, Orange=5`
- **QuestFailureCode** — `QuestNotFound, PlayerNotFound, InsufficientEnergy, PrerequisiteNotMet, DifficultyLocked`
- **RaidHitFailureCode** — `RaidNotFound, RaidExpired, RaidAlreadyDefeated, InvalidHitSize, InsufficientStamina, AccessDenied, RaidFull`
- **SummonRaidFailureCode** — `DefinitionNotFound, PlayerNotFound`
- **UseItemFailureCode** — `ItemNotFound, InsufficientItems, ItemNotUsable`

---

---

### ILegionService
`src/ROTA.Application/Interfaces/ILegionService.cs`

| Method | Description |
|--------|-------------|
| `Task<IReadOnlyList<OwnedUnitResponse>> GetOwnedUnitsAsync(Guid playerId, ct)` | Owned units hydrated with definitions |
| `Task<IReadOnlyList<OwnedLegionResponse>> GetOwnedLegionsAsync(Guid playerId, ct)` | Owned legions hydrated with definitions |
| `Task<SetActiveLegionResult> SetActiveLegionAsync(Guid playerId, string legionDefId, ct)` | Set active legion; clears IsActive on all others |
| `Task<AssignSlotResult> AssignSlotAsync(Guid playerId, string legionDefId, string family, int slotIndex, string unitDefId, ct)` | Assign unit to slot — validates owns/type/constraint/dup |
| `Task<ClearSlotResult> ClearSlotAsync(Guid playerId, string legionDefId, string family, int slotIndex, ct)` | Soft-delete slot (idempotent) |
| `Task<LegionPowerResult> ComputeLegionPowerAsync(Guid playerId, string legionDefId, ct)` | Raw legion power (no PowerScaling — display only) |
| `Task<LegionDetailResponse?> GetLegionDetailAsync(Guid playerId, string legionDefId, ct)` | Full slot layout + computed power |
| `Task<CommanderEquipResult> EquipCommanderAsync(Guid playerId, string gearDefinitionId, ct)` | Equip gear in commander slot (upsert in place); validates gear def exists |
| `Task<CommanderUnequipResult> UnequipCommanderAsync(Guid playerId, ct)` | Remove commander gear (soft-delete, idempotent) |
| `Task<CommanderGearResponse?> GetCommanderAsync(Guid playerId, ct)` | Current commander gear; null if empty |
| `Task GrantUnitAsync(Guid playerId, string unitDefinitionId, ct)` | Idempotent unit grant — re-grant of already-owned unit is a silent no-op |
| `Task GrantLegionAsync(Guid playerId, string legionDefinitionId, ct)` | Idempotent legion grant — re-grant is a silent no-op |
| `Task<BuyUnitResult> BuyUnitAsync(Guid playerId, string unitDefinitionId, ct)` | Ownership pre-check (→ AlreadyOwned 409 no charge) → SpendGems (idempotent refId) → GrantUnit |
| `Task<BuyLegionResult> BuyLegionAsync(Guid playerId, string legionDefinitionId, ct)` | Same pattern as BuyUnit; refId = `legionbuy:{playerId}:{legionId}` |

Implementation: `LegionService` (`src/ROTA.Application/Services/LegionService.cs`)
Constructor: `(IPlayerUnitRepository, IPlayerLegionRepository, IPlayerLegionSlotRepository, IUnitDefinitionProvider, ILegionDefinitionProvider, IPlayerCommanderGearRepository, IGearDefinitionProvider, IGemService, IOptions<LegionConfig>)`

**Combat note (Slice 4):** `RaidService` does NOT call `ComputeLegionPowerAsync` in combat — it computes legionPower inline (same RNG multiplier+hitSize as charBase, applies `LegionConfig.PowerScaling`). `ComputeLegionPowerAsync` is for display only.

---

### IUnitDefinitionProvider / ILegionDefinitionProvider
`src/ROTA.Application/Interfaces/I{Unit,Legion}DefinitionProvider.cs`
Singletons; `content/units.json` and `content/legions.json` loaded at startup.

| Method | Description |
|--------|-------------|
| `GetById(string id)` | Look up by id; null if not found |
| `GetAll()` | All definitions |

---

### IPlayerUnitRepository / IPlayerLegionRepository / IPlayerLegionSlotRepository / IPlayerCommanderGearRepository
`src/ROTA.Application/Interfaces/`

**IPlayerUnitRepository** — `GetOwnedAsync`, `FindAsync`, `UpsertAsync` (create or restore)
**IPlayerLegionRepository** — `GetOwnedAsync`, `FindAsync`, `GetActiveAsync`, `UpsertAsync`, `UpdateAsync`
**IPlayerLegionSlotRepository** — `GetForLegionAsync`, `FindAsync`, `UpsertAsync` (create or reassign), `SoftDeleteAsync`
**IPlayerCommanderGearRepository** — `FindAsync(playerId)` (returns any row incl. soft-deleted, since one row per player), `CreateAsync`, `UpdateAsync`

---

### LegionController — `api/units` + `api/legions` [Authorize]
`src/ROTA.Api/Controllers/LegionController.cs`

| Endpoint | Service Method | Responses |
|----------|---------------|-----------|
| `GET /api/units` | `GetOwnedUnitsAsync` | 200 |
| `GET /api/legions` | `GetOwnedLegionsAsync` | 200 |
| `PUT /api/legions/{id}/active` | `SetActiveLegionAsync` | 200, 404 |
| `PUT /api/legions/{id}/slots/{family}/{index}` | `AssignSlotAsync` | 200, 400, 404, 409, 422 |
| `DELETE /api/legions/{id}/slots/{family}/{index}` | `ClearSlotAsync` | 200 |
| `GET /api/legions/{id}` | `GetLegionDetailAsync` | 200, 404 |
| `PUT /api/legions/commander` | `EquipCommanderAsync` | 200, 404 |
| `DELETE /api/legions/commander` | `UnequipCommanderAsync` | 200 |
| `GET /api/legions/commander` | `GetCommanderAsync` | 200, 404 |
| `POST /api/units/buy` | `BuyUnitAsync` | 200, 400, 404, 409, 422 |
| `POST /api/legions/buy` | `BuyLegionAsync` | 200, 400, 404, 409, 422 |

---

### PlayerUnit / PlayerLegion / PlayerLegionSlot (Entities)
`src/ROTA.Domain/Entities/`

**PlayerUnit**: `Id, PlayerId, UnitDefinitionId, Quantity(=1), created/updated/IsDeleted`. `Create(playerId, unitDefId)`, `Restore()`.
**PlayerLegion**: `Id, PlayerId, LegionDefinitionId, IsActive(bool), created/updated/IsDeleted`. `Create(...)`, `SetActive(bool)`, `Restore()`. One IsActive=true per player (service-enforced).
**PlayerLegionSlot**: `Id, PlayerId, LegionDefinitionId, SlotFamily(LegionSlotFamily), SlotIndex(int), UnitDefinitionId, created/updated/IsDeleted`. `Create(...)`, `Reassign(unitDefId)`, `SoftDelete()`. Unique `(player_id, legion_def_id, slot_family, slot_index)`.
**PlayerCommanderGear**: `Id, PlayerId, GearDefinitionId, created/updated/IsDeleted`. `Create(playerId, gearDefId)`, `Equip(gearDefId)` (also restores soft-deleted row), `Unequip()`. Unique index on `player_id` — at most one row per player (upsert in place). In combat: only `ProcChance`/`ProcPercent` read; `BonusAttack`/`BonusDefense` never reach `EffectiveCombatData`.

---

### Content Models (Slice 1)
**UnitDefinition** (`content/units.json`): id, name, description, unitType, rarity, baseAttack, baseDefense, race, role, attribute, ability (UnitAbility?), isPassive, legionBonus, iconPath, acquisition.
**UnitAbility**: procChance (0..1), procAmount, conditions (ConditionalBonus[]). Fires in proc phase only when isPassive=false.
**LegionDefinition** (`content/legions.json`): id, name, description, rarity, powerBonus(%), generalSlots (SlotSpec[]), troopSlots (SlotSpec[]), iconPath, acquisition.
**SlotSpec**: constraintType (SlotConstraintType), constraintValue (string?).
**LegionConfig** (`appsettings.json`): PowerScaling (default 1.0), UnitCoefficients (General {2.0,0.4} Troop {1.44,0.36}), MaxUnitProcBonus (default 5.0).

### RaidService — Slice 4 additions
`RaidHitResponse` gains: `long LegionPower` (scaled legion term, 0 when no active legion), `long UnitProcBonus` (capped total unit-ability proc bonus, separate from MagicProcBonus), `List<MagicProcDTO> UnitProcs` (raw per-unit proc breakdown; reuses MagicProcDTO shape).
New injected deps: `IPlayerLegionRepository, IPlayerLegionSlotRepository, IUnitDefinitionProvider, ILegionDefinitionProvider, IOptions<LegionConfig>`.
Damage formula update: `preProc = charBase + legionPowerTerm` (then mount/magic/unit procs); `damageFinal` includes legion power → counts toward contribution.

---

## Phase 2 Backlog

| File | Description |
|------|-------------|
| `src/ROTA.Domain/Entities/PlayerStats.cs` | DiscernmentInvestment quest-drop-quality effects |
| `src/ROTA.Application/Services/EnergyService.cs` | Wire IClassService regen — not stored rate |
| `src/ROTA.Application/Services/QuestService.cs` | Explicit DB transaction scope for rewards |
| `src/ROTA.Application/Services/RaidService.cs` | On-hit drops for World/Event raid tiers |
| All | Equipment item type with stat bonuses |
| All | Consumable item type (potions, buffs) |
| All | Crafting system: Material → Equipment |
| All | Guild system: GuildStamina, guild raids |
