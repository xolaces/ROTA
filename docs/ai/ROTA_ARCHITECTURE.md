# ROTA Architecture

## CURRENT IMPLEMENTATION

The project is organized into five layers:

- `src/ROTA.Api` — web host, controllers, middleware, SignalR hubs, startup, background services
- `src/ROTA.Application` — services, validators, interfaces, job logic, gameplay rules
- `src/ROTA.Domain` — entities, domain methods, business invariants, enums
- `src/ROTA.Infrastructure` — EF Core configuration, repositories, Redis integration, content providers, services
- `src/ROTA.Shared` — DTOs and shared models

This matches the project architecture description in the repository and the current service registration pattern in `Program.cs`.

## API layer

`Program.cs` wires up:

- ASP.NET Core MVC controllers
- SignalR hub registration
- JWT bearer authentication with RS256
- authorization policies (`AdminOnly`, `ModeratorOrAdmin`)
- DB context registration
- Redis connection
- hosted background services
- middleware ordering

The middleware order is intentionally security-sensitive, with logging, CORS, rate limiting, auth, authz, and audit log ordering defined in the startup pipeline.

## Application layer

The application layer owns most of the gameplay logic:

- `AuthService`
- `RaidService`
- `QuestService`
- `StatService`
- `EnergyService`
- `ItemService`
- `SocialService`
- `GuildService`
- `GauntletService`
- `MasteryService`
- `AchievementService`

These services are scoped, and most game rules are implemented here rather than in controllers.

## Domain layer

Domain entities are stateful but intentionally encapsulate mutation via methods. Examples:

- `Player` owns role, level, progression, and resource state transitions
- `ActiveRaid` owns timer, HP, lifecycle state, participation, visibility, and defeat transitions
- `RaidParticipant` owns per-player damage and pending reward state
- `PlayerResource` owns current/max resource state

The current implementation preserves the pattern of private setters and domain methods instead of direct field mutation from application code.

## Infrastructure layer

The infrastructure layer keeps the concrete dependencies:

- EF Core `DbContext` and model configuration
- repository implementations for players, raids, guilds, inventory, rewards, and social state
- Redis-backed stores and locks
- content providers for quests, raids, loot tables, items, gear, and definitions

The repository layer is the boundary between the domain model and PostgreSQL persistence.

## Persistence and transactions

A key pattern is the advisory-lock transaction guard used in the raid system:

- `ActiveRaidRepository.AtomicApplyHitAsync` wraps the hit mutation in a PostgreSQL transaction.
- It acquires `pg_advisory_xact_lock` on the raid ID before mutating hit state.
- This serializes concurrent hits on a single raid and helps keep damage and reward updates atomic.

This is the current implementation for concurrent raid-hit correctness.

## Content model

The game content is JSON-driven and boot-validated. Providers load JSON definitions once at startup and validate them before runtime use. This includes:

- raid definitions
- quest definitions
- loot tables
- item definitions
- gear definitions
- magic definitions
- mastery definitions
- achievement definitions
- subject catalog data

This design keeps content changes deployable without changing compiled logic, while still enforcing boot-time validation for broken data.

## Security model

The server is authoritative. This is not a client-trusted system.

Key operational rules:

- JWT authentication uses RS256 and public/private key pairs.
- Refresh token rotation is enforced.
- Rate limiting is enforced per-IP and per-player.
- Audit log writes record state-changing actions.
- Background services are used for out-of-band operations like email sending and Gauntlet rank snapshotting.

## Background job model

The app owns background tasks for non-blocking side effects, for example:

- `EmailSendBackgroundService`
- `GauntletRankSnapshotService`

These are startup-registered hosted services and are used to isolate asynchronous and periodic work from the request path.

## Client boundary

The Unity client is a separate repo and is not authoritative. It sends intent and consumes server output. The server returns DTOs and authoritative state snapshots rather than trusting client-side simulation.

## INVARIANTS

The implementation establishes several architectural invariants:

- controllers stay thin and delegate to services
- domain entities mutate via methods, not raw public setters
- game state is resolved server-side
- reward and stat logic runs on the server
- raid/quest definitions are validated at startup

## UNVERIFIED INTENT

Some architectural ideas are documented in design files and specs, but the real runtime behavior remains the source of truth. Any documentation that conflicts with the working implementation should be treated as historical or aspirational rather than current contract.
