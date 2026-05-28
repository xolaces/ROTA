# ROTA Dev Journal
Personal notes — not used in code generation prompts.

---

## How This Codebase Is Structured

The project is split into four layers, each in its own C# project inside the `src/` folder. **ROTA.Domain** is the innermost layer — it holds the entities (Player, ActiveRaid, etc.) and enums. These classes describe what the game world *is*: a Player has a level and a class; a raid has HP and a difficulty. Domain entities never touch the database directly and never import anything from the layers above them. All their state changes go through explicit methods (like `player.AddExperience(...)` or `raid.TakeDamage(...)`) rather than letting other code reach in and set properties directly. This makes it easy to see all the ways a Player can legally change — just look at its public methods.

**ROTA.Application** is where all the business logic lives. It has two sub-folders: `Interfaces/` contains the contracts (e.g. `IQuestService`, `IPlayerRepository`) and `Services/` contains the implementations. The key rule is that nothing in Application touches EF Core, Redis, or the database directly — it only ever calls interfaces. This is the dependency inversion principle: Application defines what it needs (an `IPlayerRepository`) and Infrastructure delivers it. This design means you can test all your game logic without a database running — you just give the service a fake (mocked) repository. **ROTA.Infrastructure** is where the real implementations live: the EF Core `DbContext`, repository classes that talk to PostgreSQL, the Redis cache, and providers that load JSON content files from disk. **ROTA.Api** is the thinnest layer — just the ASP.NET controllers, middleware, and `Program.cs`. Controllers receive HTTP requests, validate input with FluentValidation, pull the player's ID out of the JWT, and immediately delegate to a service. They never make decisions.

A request from the Unity client follows this path: Unity sends an HTTP POST to `/api/quests/zone1_wolves/attempt`. The request hits `RequestLoggingMiddleware`, then `RateLimitMiddleware` (checks Redis), then `UseAuthentication` (validates the JWT), then `AuditLogMiddleware`, then finally `QuestController.Attempt`. The controller validates the body with FluentValidation, parses the difficulty string to the `QuestDifficulty` enum, extracts the `playerId` from the verified JWT claim, and calls `_quests.AttemptQuestAsync(playerId, questId, difficulty)`. `QuestService` then loads the quest definition from the in-memory JSON cache, checks if the player has the prerequisites, calls `EnergyService.SpendEnergyAsync` (which hits PostgreSQL with a row-level lock), computes rewards, calls `Player.AddExperience` to update the entity, calls `IPlayerRepository.UpdateAsync` to persist it, fires `StatService.GrantLevelUpPointsAsync` for each level gained, and returns a `QuestResultResponse`. The controller returns `Ok(result)`. On the way back, the response goes through no middleware — ASP.NET serializes the DTO to JSON and sends it over HTTPS to Unity.

---

## The Pattern Every System Follows

Every game system in ROTA is built as: one interface → one service implementation → one controller → one test file. QuestService is the most complete example.

**Step 1 — The interface** (`src/ROTA.Application/Interfaces/IQuestService.cs`)

The interface has exactly two methods: `GetAvailableQuestsAsync` and `AttemptQuestAsync`. This file is the contract. Nothing outside Application needs to know that `QuestService` exists — they only know about `IQuestService`. If you ever wanted to replace the implementation (say, for testing or a complete rewrite), you'd only need to implement this interface. This is also why unit tests can mock it: Moq creates a fake object that satisfies the interface without running any real code.

**Step 2 — The service** (`src/ROTA.Application/Services/QuestService.cs`)

`QuestService` is a `sealed class` that implements `IQuestService`. Its constructor lists every dependency it needs: `IPlayerRepository`, `IEnergyService`, `IGemService`, `IStatService`, `IQuestDefinitionProvider`, `IQuestProgressRepository`, `IQuestDifficultyProgressRepository`, `IAuditLogRepository`, `IPlayerInventoryRepository`. ASP.NET's DI container injects these automatically at runtime. The `AttemptQuestAsync` method contains all the game logic for what happens when a quest is attempted — it is the only place that sequence exists. No other file in the codebase handles "attempt a quest." The method: loads the quest definition, checks the player exists, checks prerequisites, checks the difficulty gate, spends energy (which can fail if the player is broke), computes rewards applying the difficulty multiplier, calls `player.AddExperience` with the XP formula lambda, saves the player, fires level-up handlers for each new level, handles sigil drops for boss quests, and writes to the audit log.

**Step 3 — The controller** (`src/ROTA.Api/Controllers/QuestController.cs`)

`QuestController` is just 65 lines. Its `Attempt` method does: parse the difficulty string from the request body → call `_quests.AttemptQuestAsync` → map the result to the correct HTTP status code. That's everything. There is no game logic here. The controller doesn't know what a prerequisite is, doesn't know how energy is spent, and doesn't know what a sigil is. It just asks the service to do the work and translates the result into HTTP. This is intentional — it keeps the controller testable at the HTTP level without having to simulate game state, and it means the business logic is reusable (another entry point, like a background job, could call `AttemptQuestAsync` directly).

**Step 4 — The unit test** (`tests/ROTA.UnitTests/Services/QuestServiceTests.cs`)

The test file builds a `QuestService` with every dependency replaced by a `Mock<T>`. The `BuildService()` helper creates all the mocks, sets up default behaviors (e.g. `energy.Setup(e => e.SpendEnergyAsync(...)).ReturnsAsync(true)` to make energy never fail), and passes them into the real `QuestService` constructor. A `MakePlayer()` helper builds a real `Player` entity (not a mock — entities are just C# objects with no DB dependency). Each test calls a method, then uses FluentAssertions to verify the result (e.g. `result.Success.Should().BeTrue()`) and verifies that the right mock was called the right number of times (e.g. `energy.Verify(e => e.SpendEnergyAsync(...), Times.Once)`). The mocks stand in for the database, Redis, and all other I/O — no network needed.

This exact pattern — interface → service → controller → test — is repeated for Auth, Energy, Player, Gems, Raids, Items, Stats, and Class. Once you understand it in QuestService, you understand all twelve systems.

---

## Every File In The Codebase

### ROTA.Domain

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Domain/Entities/Player.cs` | Entity | Core player: level, XP, gold, class, ban/delete status |
| `src/ROTA.Domain/Entities/PlayerStats.cs` | Entity | Investable stat sheet: attack, defense, health, energy/stamina/discernment investments, skill points |
| `src/ROTA.Domain/Entities/PlayerResource.cs` | Entity | One row per resource pool (Energy/Stamina/GuildStamina): checkpoint value, max, regen rate |
| `src/ROTA.Domain/Entities/PlayerInventoryItem.cs` | Entity | Single item stack in a player's inventory with quantity |
| `src/ROTA.Domain/Entities/PlayerQuestProgress.cs` | Entity | Total completion count for one player+quest combination |
| `src/ROTA.Domain/Entities/PlayerQuestDifficultyProgress.cs` | Entity | Completion count per player+quest+difficulty (gates Hard/Legendary/Nightmare unlock) |
| `src/ROTA.Domain/Entities/ActiveRaid.cs` | Entity | Live raid instance: current HP, difficulty, participant count, expiry |
| `src/ROTA.Domain/Entities/RaidParticipant.cs` | Entity | One player's total damage dealt to one active raid |
| `src/ROTA.Domain/Entities/GemTransaction.cs` | Entity | Single append-only gem ledger entry; balance is always SUM of all entries |
| `src/ROTA.Domain/Entities/AuditLog.cs` | Entity | Immutable audit record: who did what, when, from which IP, with what input hash |
| `src/ROTA.Domain/Entities/RefreshToken.cs` | Entity | Hashed refresh token with expiry, revocation timestamp, and issuing IP |
| `src/ROTA.Domain/Enums/PlayerClass.cs` | Enum | All 40+ class values across 11 tiers from Conscript to Eternal |
| `src/ROTA.Domain/Enums/ResourceType.cs` | Enum | Energy, Stamina, GuildStamina |
| `src/ROTA.Domain/Enums/StatType.cs` | Enum | The six investable stats: Energy, Stamina, Discernment, Attack, Defense, Health |
| `src/ROTA.Domain/Enums/QuestDifficulty.cs` | Enum | Normal, Hard, Legendary, Nightmare |
| `src/ROTA.Domain/Enums/RaidDifficulty.cs` | Enum | Normal, Hard, Legendary, Nightmare |
| `src/ROTA.Domain/Enums/GemTransactionType.cs` | Enum | Classifies every gem credit/debit for reporting and idempotency |
| `src/ROTA.Domain/Enums/ItemType.cs` | Enum | Equipment, Material, StatBag, Sigil, Consumable |
| `src/ROTA.Domain/Enums/ItemRarity.cs` | Enum | Grey=0 through Orange=5; Orange is the permanent ceiling |

### ROTA.Application — Configuration

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Application/Configuration/LevelingConfig.cs` | Config POCO | XP formula parameters (multiplier, exponent) and milestone floor map read from appsettings.json |
| `src/ROTA.Application/Configuration/ClassConfig.cs` | Config POCO | Regen rates per class, convergence level map, guild stamina regen — read from appsettings.json |

### ROTA.Application — Interfaces

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Application/Interfaces/IAuthService.cs` | Interface | Contract for register, login, token refresh, logout |
| `src/ROTA.Application/Interfaces/IAuthLockoutService.cs` | Interface | Contract for Redis-based lockout: increment failures, check locked, clear |
| `src/ROTA.Application/Interfaces/IPlayerService.cs` | Interface | Contract for profile GET and username update |
| `src/ROTA.Application/Interfaces/IPlayerRepository.cs` | Interface | Contract for all Player DB reads and writes |
| `src/ROTA.Application/Interfaces/IRefreshTokenRepository.cs` | Interface | Contract for refresh token create/find/revoke |
| `src/ROTA.Application/Interfaces/IAuditLogRepository.cs` | Interface | Contract for append-only audit log writes |
| `src/ROTA.Application/Interfaces/IEnergyService.cs` | Interface | Contract for live resource value, spend, refill, update max |
| `src/ROTA.Application/Interfaces/IPlayerResourceRepository.cs` | Interface | Contract for resource checkpoint reads and atomic updates |
| `src/ROTA.Application/Interfaces/IGemService.cs` | Interface | Contract for gem balance, grant, spend, daily refill |
| `src/ROTA.Application/Interfaces/IGemTransactionRepository.cs` | Interface | Contract for appending gem ledger rows and summing balance |
| `src/ROTA.Application/Interfaces/IQuestService.cs` | Interface | Contract for available quests list and quest attempt |
| `src/ROTA.Application/Interfaces/IQuestDefinitionProvider.cs` | Interface | Contract for loading static quest definitions from JSON |
| `src/ROTA.Application/Interfaces/IQuestProgressRepository.cs` | Interface | Contract for reading/upserting per-player quest completion counts |
| `src/ROTA.Application/Interfaces/IQuestDifficultyProgressRepository.cs` | Interface | Contract for per-player per-difficulty completion counts |
| `src/ROTA.Application/Interfaces/IRaidService.cs` | Interface | Contract for active raids list, summon, and hit |
| `src/ROTA.Application/Interfaces/IRaidDefinitionProvider.cs` | Interface | Contract for loading static raid definitions from JSON |
| `src/ROTA.Application/Interfaces/IActiveRaidRepository.cs` | Interface | Contract for creating, finding, and updating active raids |
| `src/ROTA.Application/Interfaces/IRaidParticipantRepository.cs` | Interface | Contract for reading/upserting per-player damage records |
| `src/ROTA.Application/Interfaces/IRaidHitCache.cs` | Interface | Contract for Redis idempotency cache on raid hits |
| `src/ROTA.Application/Interfaces/IItemService.cs` | Interface | Contract for inventory list and item use |
| `src/ROTA.Application/Interfaces/IItemDefinitionProvider.cs` | Interface | Contract for loading static item definitions from JSON |
| `src/ROTA.Application/Interfaces/IPlayerInventoryRepository.cs` | Interface | Contract for reading/upserting player inventory rows |
| `src/ROTA.Application/Interfaces/ILootTableProvider.cs` | Interface | Contract for loading loot table definitions and validating them at startup |
| `src/ROTA.Application/Interfaces/IStatService.cs` | Interface | Contract for stat allocation, level-up rewards, XP formula, and stat sheet GET |
| `src/ROTA.Application/Interfaces/IClassService.cs` | Interface | Contract for class regen rates, available choices, assign, auto-advance, convergence check |

### ROTA.Application — Services

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Application/Services/AuthService.cs` | Service | Register/login/refresh/logout with BCrypt hashing, RS256 JWT, Redis lockout, session cap |
| `src/ROTA.Application/Services/EnergyService.cs` | Service | Live resource computation from checkpoint + elapsed time; row-level lock on spend |
| `src/ROTA.Application/Services/PlayerService.cs` | Service | Profile GET with live energy values; username update with uniqueness re-check |
| `src/ROTA.Application/Services/GemService.cs` | Service | Gem balance from ledger SUM; idempotent grant/spend via unique reference IDs |
| `src/ROTA.Application/Services/QuestService.cs` | Service | Full quest attempt pipeline: energy, rewards, XP, level-ups, sigil drops, audit |
| `src/ROTA.Application/Services/RaidService.cs` | Service | Raid summon, server-RNG hit damage, contribution tiers, reward distribution |
| `src/ROTA.Application/Services/ItemService.cs` | Service | Inventory list; StatBag grants SP; Sigil summons raid and is consumed |
| `src/ROTA.Application/Services/StatService.cs` | Service | Stat allocation with LSI cap; XP formula; level-up SP+gems+class auto-advance |
| `src/ROTA.Application/Services/ClassService.cs` | Service | Class tier gating, regen lookup, convergence auto-advance, assign chosen class |

### ROTA.Application — Models

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Application/Models/QuestDefinition.cs` | Content model | Deserialization shape for one quest from quests.json |
| `src/ROTA.Application/Models/RaidDefinition.cs` | Content model | Deserialization shape for one raid from raids.json |
| `src/ROTA.Application/Models/ItemDefinition.cs` | Content model | Deserialization shape for one item from items.json |
| `src/ROTA.Application/Models/LootTableDefinition.cs` | Content model | Deserialization shape for one loot table from loot_tables.json |

### ROTA.Application — Validators

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Application/Validators/AuthValidators.cs` | Validator | FluentValidation rules for RegisterRequest, LoginRequest, RefreshRequest |
| `src/ROTA.Application/Validators/PlayerValidators.cs` | Validator | FluentValidation rules for UpdateUsernameRequest |
| `src/ROTA.Application/Validators/StatValidators.cs` | Validator | FluentValidation rules for AllocateStatRequest |

### ROTA.Infrastructure — Persistence

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Infrastructure/Persistence/RotaDbContext.cs` | EF DbContext | Registers all entity DbSets; applies all Fluent API configurations |
| `src/ROTA.Infrastructure/Persistence/RotaDbContextFactory.cs` | EF Factory | Lets `dotnet ef` run migrations without starting the full API |
| `src/ROTA.Infrastructure/ServiceCollectionExtensions.cs` | DI Registration | Wires all interfaces to their implementations for ASP.NET DI |

### ROTA.Infrastructure — EF Configurations

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Infrastructure/Persistence/Configurations/PlayerConfiguration.cs` | EF Config | Table name, column names, indexes, and relationships for Player |
| `src/ROTA.Infrastructure/Persistence/Configurations/PlayerStatsConfiguration.cs` | EF Config | Column names for all stat investment fields; 1:1 FK to players |
| `src/ROTA.Infrastructure/Persistence/Configurations/PlayerResourceConfiguration.cs` | EF Config | Column names; composite index on (player_id, resource_type) |
| `src/ROTA.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs` | EF Config | Column names; index on token_hash for fast revocation lookup |
| `src/ROTA.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs` | EF Config | Column names; append-only by convention — no update methods exist |
| `src/ROTA.Infrastructure/Persistence/Configurations/GemTransactionConfiguration.cs` | EF Config | Column names; unique partial index on (player_id, type, reference_id) for idempotency |
| `src/ROTA.Infrastructure/Persistence/Configurations/ActiveRaidConfiguration.cs` | EF Config | Column names; index on summoned_by_player_id |
| `src/ROTA.Infrastructure/Persistence/Configurations/RaidParticipantConfiguration.cs` | EF Config | Column names; unique index on (active_raid_id, player_id) |
| `src/ROTA.Infrastructure/Persistence/Configurations/PlayerQuestProgressConfiguration.cs` | EF Config | Column names; unique index on (player_id, quest_id) |
| `src/ROTA.Infrastructure/Persistence/Configurations/PlayerQuestDifficultyProgressConfiguration.cs` | EF Config | Column names; unique index on (player_id, quest_id, difficulty) |
| `src/ROTA.Infrastructure/Persistence/Configurations/PlayerInventoryItemConfiguration.cs` | EF Config | Column names; unique index on (player_id, item_definition_id) |

### ROTA.Infrastructure — Repositories

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Infrastructure/Persistence/Repositories/PlayerRepository.cs` | Repository | EF Core implementation of IPlayerRepository |
| `src/ROTA.Infrastructure/Persistence/Repositories/PlayerResourceRepository.cs` | Repository | Resource checkpoint reads; AtomicUpdateAsync uses FOR UPDATE row lock |
| `src/ROTA.Infrastructure/Persistence/Repositories/RefreshTokenRepository.cs` | Repository | EF Core implementation of IRefreshTokenRepository |
| `src/ROTA.Infrastructure/Persistence/Repositories/AuditLogRepository.cs` | Repository | Append-only audit log insert; no update or delete methods |
| `src/ROTA.Infrastructure/Persistence/Repositories/GemTransactionRepository.cs` | Repository | Ledger append and balance SUM; unique constraint enforces idempotency |
| `src/ROTA.Infrastructure/Persistence/Repositories/QuestProgressRepository.cs` | Repository | Upsert quest completion count for one player+quest |
| `src/ROTA.Infrastructure/Persistence/Repositories/QuestDifficultyProgressRepository.cs` | Repository | Upsert completion count for one player+quest+difficulty combination |
| `src/ROTA.Infrastructure/Persistence/Repositories/ActiveRaidRepository.cs` | Repository | Create and update active raid instances; no soft-delete filter so expired raids load |
| `src/ROTA.Infrastructure/Persistence/Repositories/RaidParticipantRepository.cs` | Repository | Find or create participant row; upsert damage dealt |
| `src/ROTA.Infrastructure/Persistence/Repositories/PlayerInventoryRepository.cs` | Repository | Find or create inventory row; upsert quantity |

### ROTA.Infrastructure — Services

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Infrastructure/Services/AuthLockoutService.cs` | Redis service | Increment/check/clear failed login counter in Redis (key: `auth:lockout:{email}`) |
| `src/ROTA.Infrastructure/Services/QuestDefinitionProvider.cs` | JSON loader | Loads quests.json once at startup; exposes by ID lookup |
| `src/ROTA.Infrastructure/Services/RaidDefinitionProvider.cs` | JSON loader | Loads raids.json once at startup; exposes by ID lookup |
| `src/ROTA.Infrastructure/Services/ItemDefinitionProvider.cs` | JSON loader | Loads items.json once at startup; exposes by ID lookup |
| `src/ROTA.Infrastructure/Services/LootTableProvider.cs` | JSON loader | Loads loot_tables.json at startup; validates table configs; throws on misconfiguration |
| `src/ROTA.Infrastructure/Services/RaidHitCache.cs` | Redis service | 24-hour idempotency cache for raid hits (key: `raidhit:{idempotencyKey}`) |

### ROTA.Infrastructure — Migrations

| Migration name | Applied | What it adds |
|----------------|---------|-------------|
| `InitialCreate` (20260520) | ✓ | players, player_stats, player_resources, refresh_tokens, audit_log |
| `Phase1_Systems6to8` (20260524) | ✓ | gem_transactions, player_quest_progress |
| `AddRaidSystem` (20260525) | ✓ | active_raids, raid_participants |
| `AddStatInvestmentFields` (20260525) | ✓ | energy/stamina/discernment investment columns on player_stats |
| `AddQuestDifficultySystem` (20260525) | ✓ | player_quest_difficulty_progress |
| `AddItemSystem` (20260525) | ✓ | player_inventory_items |
| `AddRaidDifficulty` (20260525) | ✓ | difficulty column on active_raids |
| `AddPlayerClass` (20260528) | ✓ | class column (int, default 0) on players |

### ROTA.Api

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Api/Program.cs` | Entry point | Registers all services, middleware pipeline, JWT config, DI wiring |
| `src/ROTA.Api/Controllers/AuthController.cs` | Controller | POST register/login/refresh/logout → IAuthService |
| `src/ROTA.Api/Controllers/PlayerController.cs` | Controller | GET+PUT /api/players/me → IPlayerService |
| `src/ROTA.Api/Controllers/QuestController.cs` | Controller | GET quests + POST attempt → IQuestService |
| `src/ROTA.Api/Controllers/RaidController.cs` | Controller | GET raids + POST summon + POST hit → IRaidService |
| `src/ROTA.Api/Controllers/ItemController.cs` | Controller | GET inventory + POST use → IItemService |
| `src/ROTA.Api/Controllers/StatController.cs` | Controller | GET/POST stats + GET/POST class → IStatService + IClassService |
| `src/ROTA.Api/Middleware/RequestLoggingMiddleware.cs` | Middleware | Logs every request (method, path, status, duration) |
| `src/ROTA.Api/Middleware/RateLimitMiddleware.cs` | Middleware | Per-IP (auth) and per-player (game) rate limiting via Redis sliding window |
| `src/ROTA.Api/Middleware/AuditLogMiddleware.cs` | Middleware | Writes one audit_log row per POST/PUT/DELETE with verified PlayerId |

### ROTA.Shared

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `src/ROTA.Shared/DTOs/AuthDTOs.cs` | DTO | RegisterRequest, LoginRequest, RefreshRequest, AuthResponse (tokens + expiry) |
| `src/ROTA.Shared/DTOs/PlayerDTOs.cs` | DTO | PlayerProfileResponse (live resources), UpdateUsernameRequest/Response |
| `src/ROTA.Shared/DTOs/QuestDTOs.cs` | DTO | QuestAvailabilityResponse, QuestAttemptRequest, QuestResultResponse (with XP fields) |
| `src/ROTA.Shared/DTOs/RaidDTOs.cs` | DTO | ActiveRaidResponse, SummonRaidRequest/Response, RaidHitRequest, RaidHitResponse, RaidRewards |
| `src/ROTA.Shared/DTOs/StatDTOs.cs` | DTO | PlayerStatsResponse, AllocateStatRequest/Response with all investment + LSI fields |
| `src/ROTA.Shared/DTOs/ClassDTOs.cs` | DTO | ClassRegenRates (current class, regen rates, available choices), ChooseClassRequest |
| `src/ROTA.Shared/DTOs/ItemDTOs.cs` | DTO | InventoryItemResponse, UseItemRequest/Response |

### Tests

| File path | Type | One-line purpose |
|-----------|------|-----------------|
| `tests/ROTA.UnitTests/Services/AuthServiceTests.cs` | Test | 12 tests: register, login, refresh, logout, lockout, session cap |
| `tests/ROTA.UnitTests/Services/EnergyServiceTests.cs` | Test | 7 tests: regen calc, cap, spend success/fail, double-spend guard |
| `tests/ROTA.UnitTests/Services/PlayerServiceTests.cs` | Test | 8 tests: profile with live energy, username update/taken/notfound |
| `tests/ROTA.UnitTests/Services/GemServiceTests.cs` | Test | 5 tests: balance sum, duplicate reference rejection, insufficient, daily |
| `tests/ROTA.UnitTests/Services/QuestServiceTests.cs` | Test | 19 tests: prereqs, difficulty gates, XP carry-over, chain level-ups, loot, sigil |
| `tests/ROTA.UnitTests/Services/RaidServiceTests.cs` | Test | 22 tests: summon, hit, damage formula, idempotency, contribution tiers, expired |
| `tests/ROTA.UnitTests/Services/ItemServiceTests.cs` | Test | 8 tests: inventory, StatBag SP grant, Sigil summon+consume, not found |
| `tests/ROTA.UnitTests/Services/StatServiceTests.cs` | Test | 16 tests: allocate stats, LSI cap, level-up gems, XP formula, AddUnassigned |
| `tests/ROTA.UnitTests/Services/ClassServiceTests.cs` | Test | 43 tests: class gates, auto-advance paths + convergence, regen rates |
| `tests/ROTA.IntegrationTests/UnitTest1.cs` | Integration test | Boots real API via WebApplicationFactory + Testcontainers; smoke test |

---

## What Each Entity Stores

**Player** represents a registered player account — the top-level object that ties everything else together. Its most important fields are `Level` and `Experience` (XP *toward the next level*, not cumulative total), `Gold`, and `Class`. It also holds security fields (`PasswordHash`, `IsBanned`, `IsDeleted`) and soft membership fields (`GuildId`, `GuildRank`) that aren't used yet. The domain methods let you do everything a player account can do: gain XP (with automatic level-up processing), earn gold, change username, get banned, get soft-deleted, and change class tier.

**PlayerStats** is a 1:1 companion to Player that holds the player's investable stat sheet. It tracks how many SkillPoints the player has unspent, and how many they've invested into each of the six investable stats: Energy, Stamina, Discernment, Attack, Defense, and Health. Energy and Stamina investments expand the player's resource pools; Attack, Defense, and Health directly increase combat values; Discernment is tracked but its game effects are deferred to Phase 2. The computed methods (`ComputeMaxEnergy`, `ComputeLSI`) do the math so services don't have to inline formulas.

**PlayerResource** stores one resource pool for one player — there are three rows per player (Energy, Stamina, GuildStamina). The key thing to understand is that `CurrentValue` is a *checkpoint*, not the live value. When you want the player's actual energy, you compute it as `checkpoint + (minutesSinceCheckpoint × regenPerMinute)`, capped at `MaxValue`. `SaveCheckpoint` is called every time the player spends or refills, storing the new value and the current timestamp. This means the database doesn't need to be updated every minute as energy regenerates.

**ActiveRaid** represents a live raid that players can hit. It starts with `CurrentHp = MaxHp` and counts down. `IsDefeated` flips to true when HP reaches zero. `ParticipantCount` is deliberately denormalized — it's incremented the first time each player hits so the raid list can show participant counts without a GROUP BY query on RaidParticipants. `ExpiresAt` is set at summon time; the service rejects hits against expired raids.

**RaidParticipant** is one row per (raid, player) pair, tracking total `DamageDealt`. When the raid is killed, the service reads all participants and their damage totals, sorts them, and uses the damage percentages to assign contribution tiers (Legendary 1/2/3, Epic, Rare, Participant). The reward multiplier for each player comes from their tier.

**GemTransaction** is an append-only ledger entry. The gem balance is never stored as a single number — it's always computed as `SELECT SUM(amount)` from all transactions for a player. Credits have positive amounts; debits have negative amounts. `ReferenceId` is a string that gates idempotency: the database has a unique partial index on `(player_id, transaction_type, reference_id)` so the same reward can never be granted twice, even if the server crashes and retries the operation.

**AuditLog** is immutable by design — nothing in the codebase ever calls UPDATE or DELETE on it. Every state-changing API call writes a row recording who did what, when, from which IP address, and with a SHA256 hash of the request body. The middleware layer handles the HTTP-level writes; services write additional rows for domain events like level-ups and class changes.

**RefreshToken** stores a hashed copy of a 7-day refresh token. When a refresh happens, the old token is immediately revoked (RevokedAt set), a new token is created, and both the old and new token hashes are in the database. The max 3 concurrent sessions rule is enforced by counting non-revoked tokens per player at login time.

**PlayerQuestProgress** and **PlayerQuestDifficultyProgress** both track completion counts, but at different granularities. Progress tracks the total across all difficulties; DifficultyProgress tracks each difficulty separately. DifficultyProgress is what gates Hard/Legendary/Nightmare — you can't attempt Hard until Normal completions ≥ 1, and so on.

**PlayerInventoryItem** is one row per (player, item definition) pair. Quantity is the stack size. The unique index means you can't have two rows for the same item — you upsert instead, incrementing quantity. When quantity reaches zero the row stays but is filtered out of inventory responses.

---

## The XP and Class System Explained

**XP carry-over** works differently from the classic "cumulative XP" model. Instead of storing total lifetime XP, the `Experience` field stores only the XP *toward the current level*. When you call `player.AddExperience(500, xpToNextLevel)`, the method adds 500 to `Experience`, then checks if it equals or exceeds `xpToNextLevel(currentLevel)`. If it does, it subtracts the threshold, increments `Level`, and checks again — looping until the remaining XP is less than the next threshold. The method returns a list of every new level reached (e.g. `[2, 3, 4]` for a triple level-up). The caller fires `GrantLevelUpPointsAsync` once for each entry in that list. The carry-over matters: if you need 1000 XP for level 2 and you have 950 XP and earn 100, you reach level 2 with 50 XP already banked toward level 3.

**Milestone floors** are minimum XP thresholds that kick in at high levels when the formula would otherwise produce too-low numbers. The formula is `round(30 × level^0.7)`. At level 1 that gives 30 XP per level, at level 100 it gives ~754, and at level 500 it gives ~2326. But the game design wants level 500 to feel like a major wall — so the config sets a floor of 3000 for levels 500+. The formula result (2326) falls below 3000, so the floor kicks in and `XpToNextLevel(500)` returns 3000. The floor only constrains where the formula falls below it: at level 100, the formula gives ~754 which exceeds the floor of 500, so the formula wins. Floors are defined in `appsettings.json` and correspond to each class convergence gate (L500, L1000, L2000, L5000, L7500, L10000, L15000, L25000).

**The class path system** operates in tiers. Every player starts as Conscript (Tier 1). At level 5 they choose one of three paths — Ironguard (stamina-heavy, for raids), Arcanist (energy-heavy, for quests), or Sentinel (balanced). At level 100 they choose a specialization within their path: Ironguard splits into Stormguard/Bloodguard/Siegebreaker, Arcanist splits into HighArcanist/ShadowArcanist/Runecaller, Sentinel splits into IroncladSentinel/Voidwalker/Dawnblade. These choices are manual — the game presents options and the player picks. At level 500 the path advances automatically to the Legendary prefix (LegendaryIronguard, etc.), and at level 1000 to the Ascendant prefix. The prefix-stripping trick: a LegendaryIronguard's regen rates are looked up by stripping "Legendary" to get "Ironguard", then looking up Ironguard's entry in the config. This means you don't need 33 separate regen entries — just the 13 base classes.

**Convergence at L2000** ends the path system. All 33 Ascendant variants collapse into a single title: Luminary. A player who was AscendantDawnblade becomes Luminary at exactly level 2000; so does an AscendantStormguard. From this point forward there are no paths, no choices — just a single class that auto-advances at each milestone. Luminary → Immortal at L5000 → Archon at L7500 → Ancient at L10000 → ElderAncient at L15000 → Eternal at L25000. Regen rates improve at each convergence gate: Luminary gets 2.5 min/pt, Eternal gets 1.0 min/pt. The `IsConvergedClass` method returns `true` for any class with an enum value ≥ 300, which is the integer cutoff for the Luminary+ tier.

---

## Current API Surface

| Method | Route | Auth required | What it does |
|--------|-------|:---:|-------------|
| POST | `/api/auth/register` | No | Create a new player account; returns access + refresh tokens |
| POST | `/api/auth/login` | No | Authenticate; returns tokens; lockout after 5 failures |
| POST | `/api/auth/refresh` | No | Rotate refresh token; returns new access + refresh pair |
| POST | `/api/auth/logout` | Yes | Revoke the provided refresh token, ending the session |
| GET | `/api/players/me` | Yes | Full profile with live resource values (not stored checkpoint) |
| PUT | `/api/players/me` | Yes | Change username; 409 if already taken |
| GET | `/api/quests` | Yes | List quests available to this player at their current level/progress |
| POST | `/api/quests/{questId}/attempt` | Yes | Attempt a quest at the given difficulty; spends energy, grants rewards |
| GET | `/api/raids` | Yes | List all active (non-expired, non-defeated) raids with caller's contribution |
| POST | `/api/raids/{raidDefinitionId}/summon` | Yes | Summon a new raid instance at the given difficulty |
| POST | `/api/raids/{activeRaidId}/hit` | Yes | Deal damage to a raid; requires hitSize ∈ {1, 5, 20}; idempotency key required |
| GET | `/api/items` | Yes | Return the caller's full inventory with item definition details |
| POST | `/api/items/{itemDefinitionId}/use` | Yes | Use an item: StatBag grants SP, Sigil summons a raid and is consumed |
| GET | `/api/stats/me` | Yes | Full stat sheet: all investments, computed caps, LSI, health |
| POST | `/api/stats/allocate` | Yes | Invest SkillPoints into a stat type; enforces LSI cap for Energy/Stamina |
| GET | `/api/stats/class` | Yes | Current class, regen rates, and list of available class choices |
| POST | `/api/stats/class/choose` | Yes | Choose a class from the available options; 403 if not eligible |

---

## What's Not Built Yet

Phase 2 adds systems that build directly on what exists now, rather than starting from scratch.

**The guild system** is the largest pending item and touches the most existing code. The `Player` entity already has `GuildId` and `GuildRank` fields — they're just null. `PlayerResource` already has a `GuildStamina` row seeded for every player, with a max value of 1 and zero regen. The guild system would introduce a `Guild` entity and table, fill in those player fields, change the GuildStamina max to `player.Level` (already the formula per the design doc), set a regen rate, and add guild raid functionality. Guild raids would be a new raid type that only guild members can hit, spending GuildStamina instead of regular Stamina. The loot table system is already built to support it — you'd add a `GuildRaid` tier value to `RaidDifficulty` and configure the rewards in `loot_tables.json`.

**The EnergyService regen fix** is the most pressing technical debt. Right now, `PlayerResource.RegenPerMinute` is a stored integer set at player creation — every player regenerates at the same rate regardless of class. The `ClassConfig` already defines per-class regen rates (e.g. Arcanist regenerates energy faster than Ironguard), and `IClassService.GetRegenRates` can look them up. What's missing is the wire-up: `EnergyService.GetCurrentEnergyAsync` needs to call `IClassService.GetRegenRates(player.Class)` and use the returned `EnergyRegenMinutesPerPoint` multiplied by the player's `EnergyInvestment` to get the actual regen rate, instead of reading the stored integer. Until this is wired up, all players regenerate at their creation-time rate.

**Discernment effects** are defined in `PlayerStats` (the field exists, SP can be invested), but the bonuses aren't applied anywhere yet. The design intent is that Discernment improves quest loot drop quality and increases the raid critical damage chance. Both effects would be applied in `QuestService.AttemptQuestAsync` and `RaidService.HitRaidAsync` — the services already have access to `PlayerStats`, so it's a matter of reading `DiscernmentInvestment` and feeding it into the reward and damage calculations.

**Equipment items** would add stat bonuses on top of the base invested stats. The `ItemType.Equipment` enum value exists, and `UseItemFailureCode.ItemNotUsable` is already returned for equipment in `ItemService`. The design intent is that equipped gear can push effective stats (including LSI) above the player-investable cap — gear is the reward for hard content, not gated by the same formula as SkillPoint investment.

**The content pipeline refactor** is the remaining pre-Phase-2 task. Currently each JSON file (quests, raids, items, loot tables) is loaded by a separate provider class with bespoke parsing logic. The refactor would introduce an `IContentLoader` abstraction, unify the loading pattern, reorganize the `content/` folder structure, and add sigil templates so new bosses can be added to `quests.json` without manually creating eight sigil item definitions.

---

## Personal C# Notes

*(Fill this in as you read through the code. Start with `QuestService.cs` — it's the most complete example of the full pattern.)*
