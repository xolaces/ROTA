# ROTA System Map

## CURRENT IMPLEMENTATION

This project is best understood as a layered game system where the server resolves everything.

## Player state

Source systems:

- `Player`
- `PlayerStats`
- `PlayerResource`
- `PlayerInventoryItem`
- `PlayerGear`
- `PlayerMagic`
- `PlayerUnit`
- `PlayerLegion`
- `PlayerMastery`
- `AchievementProgress`

These systems establish identity, progression, inventory, and resource state. They are read and updated through repositories and service-layer orchestration.

## Progression systems

Major relationships:

- `Player.AddExperience` -> stats service -> level-up allocation -> resource max sync
- `IStatService.GrantLevelUpPointsAsync` -> `PlayerStats` investment updates -> max energy/stamina/health recomputation
- `PlayerResource` values are derived/checked against max values and regenerated over time

Important files:

- `src/ROTA.Domain/Entities/Player.cs`
- `src/ROTA.Application/Services/StatService.cs`
- `src/ROTA.Application/Services/EnergyService.cs`

## Combat and raid flow

Flow:

- summon raid -> validate content -> create `ActiveRaid`
- hit raid -> check access, expiry, idempotency, spend currency
- compute damage -> update participant damage -> check kill conditions
- if kill: distribute rewards, flip raid to `Lootable`, persist reward data
- loot claim: grant deferred items/gems/drops, mark claimed, dismiss the raid when fully claimed

Important files:

- `src/ROTA.Application/Services/RaidService.cs`
- `src/ROTA.Domain/Entities/ActiveRaid.cs`
- `src/ROTA.Domain/Entities/RaidParticipant.cs`
- `src/ROTA.Infrastructure/Persistence/Repositories/ActiveRaidRepository.cs`

## Quest flow

Quest logic tracks chapter/zone/node progression, energy costs, completion counts, and resets.

Important relationships:

- quest attempt -> energy spend -> difficulty gate -> node clear -> unlock progression -> reward settlement
- zone reset can re-open node state without erasing the permanent unlock tracking used for forward gating

## Economy and rewards

Key systems:

- append-only gem ledger
- player gold and level rewards
- inventory, sigils, and item grants
- gear drops and threshold-based reward ladders
- guild economy and gauntlet economy hooks

Important files:

- `src/ROTA.Application/Services/GemService.cs`
- `src/ROTA.Application/Services/ItemService.cs`
- `src/ROTA.Application/Services/RaidService.cs`
- `src/ROTA.Infrastructure/Services/LootTableProvider.cs`

## Social and guild systems

Relationships:

- guild membership -> guild raid access -> guild contribution -> guild economy
- block/friendship/private message system -> chat visibility + moderation

Important files:

- `src/ROTA.Application/Services/SocialService.cs`
- `src/ROTA.Application/Services/GuildService.cs`
- `src/ROTA.Api/SignalR/ChatHub.cs`

## Content and validation

Content providers read JSON definitions once and validate them at startup. They are central to the game because many rules are content-driven rather than hardcoded.

High-risk cross-system relationships

1. Combat -> damage -> raid contribution -> raid rewards -> inventory/progression
2. Player state -> stats -> combat -> rewards -> mastery/achievements
3. Quest progression -> zone unlocks -> boss rewards -> item and sigil drops
4. Guild membership -> guild raid access -> guild stamina -> raid rewards
5. Player identity -> moderation -> ban/mute -> chat/social gate checks

## Important tests

The following types of tests are central to the project’s safety net:

- raid logic tests
- timer and expiry tests
- concurrency tests
- inventory and item use tests
- stat allocation tests
- guild and social tests
- gauntlet scoring and ladder tests

Key verified examples in the current worktree include the raid timer tests for World-raid behavior and the existing raid service unit/integration coverage.
