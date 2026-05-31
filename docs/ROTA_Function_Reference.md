# ROTA Function Reference
Last updated: 2026-05-30 (v0.2.5)
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
| `Task<bool> SpendEnergyAsync(Guid playerId, ResourceType, int amount, CancellationToken)` | Deduct with row lock (participates in ambient tx when inside advisory-lock callback) |
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
Server-seeded RNG damage. Redis idempotency (24h TTL). Contribution tiers → reward multipliers. Level-ups same pattern as QuestService. Damage pipeline: base → proc → crit → FlatDamagePercent → TakeDamage. Stamina spend is inside the advisory-lock transaction (atomic with hit).

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
