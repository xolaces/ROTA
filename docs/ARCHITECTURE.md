# ROTA — Architecture (enforced decisions only)

*Verified 2026-05-29 by source tracing. Only decisions actually enforced by the codebase are
listed. Aspirational/game-design choices live in `docs/DESIGN_NORTHSTAR.md`, not here.*

## Layering (compiler-enforced via project references)
```
Domain         ← depends on nothing
Application    ← Domain
Infrastructure ← Domain            (EF configs, repositories, Redis, seeding)
Api            ← Application (+ Infrastructure for DI wiring)
Shared         ← DTOs / cross-cutting contracts
```
A Domain→Infrastructure reference will not compile. This is the primary structural guarantee.

## Domain entities (High)
- Private setters; state changes only through factory methods (`Create`, `CreateWithId`) and
  domain methods (`GrantRole`, `AddExperience`, `Redeem`, …). No public setters.
- No EF attributes on entities. Mapping is **Fluent API only**, in
  `Infrastructure/Persistence/Configurations/*`.
- Computed values (gem balance, effective stats, live energy) are never stored — derived on read.

## Persistence (High)
- PostgreSQL 16 + EF Core 9 (Npgsql). **snake_case** for all tables/columns/indexes.
- Every table: `id` (UUID), `created_at`, `updated_at`, `is_deleted`. FKs are indexed.
- Migrations are the schema source of truth (`Infrastructure/Migrations`, 11 to date).
- **EF enum + store-default rule:** if a property has `HasDefaultValue(X)` and the CLR default (0)
  differs from X, you MUST set `HasSentinel(X)` or the CLR-default value is silently replaced by
  the store default. (Learned via the `RaidSize.Size` / `PlayerRoles.Roles` bug.)
- Concurrency-critical writes use a PostgreSQL transaction-level advisory lock +
  `ChangeTracker.Clear()` (`ActiveRaidRepository.AtomicApplyHitAsync`). Single-use claims use one
  atomic conditional `UPDATE … WHERE <unclaimed>` (`BetaKeyRepository.TryRedeemAsync`).

## Security (High — traced)
- **RS256 JWT only.** `ValidAlgorithms` whitelists RsaSha256; `ClockSkew = 0`; `ValidateLifetime`.
  HS256 is banned.
- Access token 15 min; refresh 7 days, rotated on every use (old revoked); max 3 sessions.
- Passwords BCrypt(12); static dummy hash defeats timing/enumeration.
- `PlayerId` always comes from the verified JWT `sub` claim — never route/body.
- Authorization is **role-claim based**: JWT carries one `role` claim per set `PlayerRoles` flag.
  Policies: `AdminOnly` (Admin role OR `Admin:PlayerIds` break-glass allowlist) and
  `ModeratorOrAdmin`. Role-mutation endpoints re-verify the actor against the DB (defense in depth).
- `audit_log` is append-only (no UPDATE/DELETE); every state change writes to it.
- Inputs validated with FluentValidation before the service layer.

## Pipeline order (security-critical, High)
exception handler → HTTPS → request logging → CORS → **rate limit** → routing → authN → authZ →
audit → endpoints. Rate limiting runs *before* authentication (drop bots before DB work).

## DI / runtime gotchas (High — cost real debugging time)
- `IConnectionMultiplexer` is a **factory singleton** (deferred resolution) so test config
  overrides apply before the connection string is read. Do NOT make it eager.
- The Admin CLI hook (`AdminCli`) calls `builder.Build()` and MUST sit **after all service
  registration** (incl. the Redis factory); in Development `ValidateOnBuild` otherwise fails on
  Redis-backed services.
- The web host runs the admin seeder at startup, which queries `players`. **Migrations must be
  applied before the app starts** on a fresh DB (the host does not auto-migrate; the CLI does).

## Content (High)
Static game definitions (quests, raids, items, loot tables) load from `src/ROTA.Api/content/*.json`
at startup via provider interfaces — not from the database.

## Testing (High)
xUnit + Moq + FluentAssertions. Unit tests mock all dependencies (fast, no Docker). Integration
tests use Testcontainers (fresh Postgres/Redis per class) via `WebApplicationFactory<Program>`,
and must stay hermetic against developer user-secrets (e.g., `Seed:AdminPassword` is neutralized).
**Caveat:** mock-only unit tests can pass while DB-layer behavior is wrong — cover store-default /
enum / persistence behavior with integration tests.
