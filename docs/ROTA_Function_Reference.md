# ROTA Function Reference
Last updated: 2026-06-03 (System 17 Leaderboards Slice 5 — Stat board snapshot)
Update when adding public methods or entities.

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
| `Task<PlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken)` | Full profile, live values. Each resource carries class-based `RegenMinutesPerPoint` (double) + `SecondsToNextPoint` (int) for client refill timers; legacy `RegenPerMinute` (int) is vestigial. |
| `Task<UpdateUsernameResult> UpdateUsernameAsync(Guid, UpdateUsernameRequest, CancellationToken)` | Username update |

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
| `Task<EffectiveCombatData> GetEffectiveCombatDataAsync(Guid, int baseAtk, int baseDef, CancellationToken)` | Effective stats + proc + conditional bonuses for combat |

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

**IRaidParticipantRepository** — find/upsert participant damage records

**IPlayerInventoryRepository** — `GetAllForPlayerAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`

**IPlayerEquipmentRepository** — `FindBySlotAsync`, `GetEquippedAsync`, `CreateAsync`, `UpdateAsync`

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
Equip/unequip/list gear. `GetEffectiveCombatDataAsync`: sums base gear stats, evaluates all `ConditionalBonuses` from equipped gear against player inventory (per-hit, indexed), folds results into effective ATK/DEF/proc/FlatDamagePercent. ProcChanceFlat clamped to 1.0 after accumulation.
Constructor: `(IPlayerEquipmentRepository, IGearDefinitionProvider, IAuditLogRepository, IPlayerInventoryRepository, IItemDefinitionProvider)`

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
