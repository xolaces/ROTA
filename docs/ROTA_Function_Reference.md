# ROTA Function Reference
Last updated: 2026-05-28
Update when adding public methods or entities.

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
| `Task<bool> SpendEnergyAsync(Guid playerId, ResourceType, int amount, CancellationToken)` | Deduct with row lock |
| `Task RefillEnergyAsync(Guid playerId, ResourceType, int amount, CancellationToken)` | Add up to max |
| `Task UpdateMaxAsync(Guid playerId, ResourceType, int newMax, CancellationToken)` | Update pool max value |

---

### IGemService
`src/ROTA.Application/Interfaces/IGemService.cs`

| Method | Description |
|--------|-------------|
| `Task<int> GetBalanceAsync(Guid playerId, CancellationToken)` | Balance from ledger sum |
| `Task<bool> GrantGemsAsync(Guid, int, GemTransactionType, string? referenceId, CancellationToken)` | Credit gems idempotently |
| `Task<bool> SpendGemsAsync(Guid, int, GemTransactionType, string? referenceId, CancellationToken)` | Debit gems idempotently |
| `Task<bool> DailyRefillAsync(Guid playerId, CancellationToken)` | Once-per-day 5 gems |

---

### IPlayerService
`src/ROTA.Application/Interfaces/IPlayerService.cs`

| Method | Description |
|--------|-------------|
| `Task<PlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken)` | Full profile live values |
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

### IClassService *(to be added — Section B)*
`src/ROTA.Application/Interfaces/IClassService.cs`

| Method | Description |
|--------|-------------|
| `ClassRegenRates GetRegenRates(PlayerClass)` | Regen rates for class |
| `IReadOnlyList<PlayerClass> GetAvailableChoices(int level, PlayerClass)` | Valid class choices |
| `Task<PlayerClass> AssignClassAsync(Guid, PlayerClass, CancellationToken)` | Assign chosen class |
| `PlayerClass ComputeAutoAdvance(int level, PlayerClass)` | Auto-advance check |
| `bool IsConvergedClass(PlayerClass)` | True if Luminary+ |

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

**IPlayerResourceRepository** — `AtomicUpdateAsync` (row-level FOR UPDATE lock)

**IAuditLogRepository** — `AppendAsync(AuditLog, CancellationToken)` (append-only)

**IRefreshTokenRepository** — find/create/revoke refresh tokens

**IGemTransactionRepository** — append gem ledger entries, sum balance

**IQuestProgressRepository** — find/upsert quest completion records

**IQuestDifficultyProgressRepository** — per-difficulty completion counts

**IActiveRaidRepository** — find/create/update active raids

**IRaidParticipantRepository** — find/upsert participant damage records

**IPlayerInventoryRepository** — find/upsert inventory items

---

## Services

### AuthService → IAuthService
`src/ROTA.Application/Services/AuthService.cs`
Implements: register/login/refresh/logout. BCrypt(12) hashing, RS256 JWT. Redis lockout (5 fails → 15 min). Max 3 concurrent sessions — revokes oldest on 4th.

### EnergyService → IEnergyService
`src/ROTA.Application/Services/EnergyService.cs`
Live value = checkpoint + elapsed × regenPerMinute, capped at max. SpendEnergy uses FOR UPDATE row lock.

### PlayerService → IPlayerService
`src/ROTA.Application/Services/PlayerService.cs`
Profile with live resource values via IEnergyService. Username update with uniqueness re-check.

### GemService → IGemService
`src/ROTA.Application/Services/GemService.cs`
Balance = SUM(amount) from ledger. Idempotency via unique index on (player_id, type, reference_id).

### StatService → IStatService
`src/ROTA.Application/Services/StatService.cs`
LSI cap 9.0 for Energy/Stamina. XpToNextLevel: `max(floor, round(30 × level^0.7))`.
Constructor: `(IPlayerRepository, IEnergyService, IGemService, IAuditLogRepository, IOptions<LevelingConfig>)`

### QuestService → IQuestService
`src/ROTA.Application/Services/QuestService.cs`
Static definitions from content/quests.json. Energy spent first. Level-ups via `player.AddExperience(xp, _stats.XpToNextLevel)`.

### RaidService → IRaidService
`src/ROTA.Application/Services/RaidService.cs`
Server-seeded RNG damage. Redis idempotency (24h TTL). Contribution tiers → reward multipliers. Level-ups same pattern as QuestService.

### ItemService → IItemService
`src/ROTA.Application/Services/ItemService.cs`
StatBag: unassigned SkillPoints. Sigil: summon raid + consume item.

### ClassService → IClassService *(to be added — Section B)*
`src/ROTA.Application/Services/ClassService.cs`
Path tiers L5-1000. Convergence L2000+. Strip Legendary/Ascendant prefix for regen lookup.

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
| `GET /api/stats/class` *(Section B)* | `GetClassInfoAsync` | 200, 404 |
| `POST /api/stats/class/choose` *(Section B)* | `AssignClassAsync` | 200, 400, 403 |

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
| `Class` | `PlayerClass` *(Section B)* |
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
| `IReadOnlyList<int> AddExperience(long, Func<int,int> xpToNextLevel)` | XP carry-over, returns new levels |
| `void AddGold(long)` | Increase gold balance |
| `void UpdateUsername(string)` | Change username |
| `void Ban(string reason)` | Set banned flag |
| `void SoftDelete()` | Mark deleted |
| `void SetClass(PlayerClass)` *(Section B)* | Assign class tier |

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

### Other Entities (summary)

**RefreshToken** — `Id, PlayerId, TokenHash, ExpiresAt, RevokedAt, IpAddress, IsDeleted`

**AuditLog** — `Id, PlayerId, Action, Timestamp, InputHash, ResultSummary, IpAddress`; `AuditLog.Create(...)` factory

**GemTransaction** — `Id, PlayerId, Amount, TransactionType, ReferenceId, CreatedAt`; append-only ledger

**PlayerQuestProgress** — `Id, PlayerId, QuestId, CompletionCount, UpdatedAt`

**PlayerQuestDifficultyProgress** — `Id, PlayerId, QuestId, Difficulty, CompletionCount, UpdatedAt`

**RaidParticipant** — `Id, ActiveRaidId, PlayerId, DamageDealt, UpdatedAt`

**PlayerInventoryItem** — `Id, PlayerId, ItemDefinitionId, Quantity, UpdatedAt`

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

### Other Enums
- **ResourceType** — `Energy, Stamina, GuildStamina`
- **StatType** — `Energy, Stamina, Discernment, Attack, Defense, Health`
- **GemTransactionType** — `DailyRefill, QuestReward, RaidReward, LevelUpReward, Purchase, Spend`
- **QuestDifficulty** — `Normal, Hard, Legendary, Nightmare`
- **RaidDifficulty** — `Normal, Hard, Legendary, Nightmare`
- **ItemType** — `Equipment, Material, StatBag, Sigil, Consumable`
- **ItemRarity** — `Grey=0, White=1, Green=2, Blue=3, Purple=4, Orange=5`
- **QuestFailureCode** — `QuestNotFound, PlayerNotFound, InsufficientEnergy, PrerequisiteNotMet, DifficultyLocked`
- **RaidHitFailureCode** — `RaidNotFound, RaidExpired, RaidAlreadyDefeated, InvalidHitSize, InsufficientStamina`
- **SummonRaidFailureCode** — `DefinitionNotFound, PlayerNotFound`
- **UseItemFailureCode** — `ItemNotFound, InsufficientItems, ItemNotUsable`

---

## Phase 2 Backlog

| File | Description |
|------|-------------|
| `src/ROTA.Domain/Entities/PlayerStats.cs` | DiscernmentInvestment quest/raid effects |
| `src/ROTA.Application/Services/EnergyService.cs` | Wire IClassService regen — not stored rate |
| `src/ROTA.Application/Services/QuestService.cs` | Explicit DB transaction scope for rewards |
| `src/ROTA.Application/Services/RaidService.cs` | Explicit DB transaction scope for rewards |
| `src/ROTA.Application/Services/RaidService.cs` | On-hit drops for World/Event raid tiers |
| All | Equipment item type with stat bonuses |
| All | Consumable item type (potions, buffs) |
| All | Crafting system: Material → Equipment |
| All | Guild system: GuildStamina, guild raids |
