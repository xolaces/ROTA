# ROTA — Rise of the Ancients

## Stack
- ASP.NET Core 10 / C#
- PostgreSQL 16 (EF Core 9, Npgsql)
- Redis 7 (StackExchange.Redis)
- JWT RS256 auth
- xUnit + Moq + FluentAssertions

## Project structure
src/ROTA.Api            ← Controllers, middleware, Program.cs
src/ROTA.Application    ← ALL business logic, service interfaces
src/ROTA.Domain         ← Entities only, no EF attributes
src/ROTA.Infrastructure ← EF Core configs, migrations, Redis
src/ROTA.Shared         ← DTOs, constants, enums

## Architecture rules (non-negotiable)
- Domain entities: private setters, no EF attributes, changes via methods only
- EF mapping: Fluent API only, never data annotations, configs in Infrastructure/Persistence/Configurations/
- Services: interface in Application/Interfaces/, implementation in Application/Services/
- Controllers: thin, delegate only, zero business logic
- snake_case for ALL PostgreSQL table names, column names, index names
- Every table has: id (UUID gen_random_uuid()), created_at, updated_at, is_deleted
- Every FK has an index

## Security rules (non-negotiable)
- Server is always authoritative — client sends intent only, server resolves
- RS256 JWT only — never HS256 ever
- Access tokens: 15 min expiry, zero clock skew
- Refresh tokens: 7 day expiry, rotate on every use, revoke old immediately
- Max 3 concurrent sessions per player — 4th login revokes oldest
- Failed login: 5 attempts → 15 min lockout → written to audit_log
- All inputs validated with FluentValidation BEFORE service layer
- Every state change writes to audit_log (PlayerId, Action, Timestamp, InputHash, ResultSummary, IpAddress)
- Audit log is append-only — no UPDATE/DELETE permission on that table ever
- Rate limit per-player AND per-IP via Redis

## Code labeling rules
- // BETA — current implementation, known limitations
- // PHASE-2 — deliberately deferred
- // FINAL — complete and hardened
- // BETA-PLACEHOLDER — stub that must be replaced before ship
- Never leave silent stubs that look complete

## Current build status
Phase 0: COMPLETE
- Solution scaffold, docker-compose, all 7 projects
- Domain entities: Player, PlayerStats, PlayerResource, RefreshToken, AuditLog
- EF Core configs + InitialCreate migration (applied)
- API pipeline: Program.cs with correct middleware order, JWT RS256 config
- Middleware stubs: RequestLoggingMiddleware, RateLimitMiddleware, AuditLogMiddleware
- IAuthService interface defined, AuthDTOs defined
- Keys: RS256 key pair generated and stored in Secret Manager

Phase 1 — Systems 1-9: COMPLETE (2026-05-25)
Build: 0 errors, 0 warnings. Tests: 60/60 passing.

System 1 — AuthService (BETA)
- AuthService: register/login/refresh/logout with BCrypt(12), RS256 JWT, token rotation
- Redis lockout: 5 failed logins → 15-min lockout per email (auth:lockout:{email})
- Max 3 concurrent sessions enforced; oldest revoked on 4th login
- Audit log written on every operation (success and failure)
- New interfaces: IAuditLogRepository, IAuthLockoutService
- New infra: AuditLogRepository, AuthLockoutService (Redis)
- Domain additions: AuditLog.Create(), Player.Ban(), Player.SoftDelete(), PlayerResource.SaveCheckpoint()
- Tests: 12 unit tests covering all success and security-failure paths

System 2 — AuthController (BETA)
- POST /api/auth/register → 201/409/400
- POST /api/auth/login    → 200/401/400
- POST /api/auth/refresh  → 200/401/400
- POST /api/auth/logout   → 204/401/400  [Authorize] required
- FluentValidation wired via injected IValidator<T>; validators registered in AddRotaServices()

System 3 — RateLimitMiddleware (BETA)
- Per-IP on /api/auth/**:  10 req/min (ratelimit:ip:{ip}:{path})
- Per-player on all else:  60 req/min (ratelimit:player:{playerId})
- Player ID extracted from JWT without signature verification (auth middleware enforces validity)
- 429 + Retry-After header; breaches written to audit_log

System 4 — AuditLogMiddleware (BETA)
- POST/PUT/DELETE requests: SHA256 hash of body, PlayerId from verified JWT, HTTP status
- Runs after authentication so PlayerId is always the verified identity
- Audit failure is swallowed — never breaks the response pipeline

System 5 — EnergyService (BETA)
- Interface: IEnergyService (GetCurrentEnergyAsync, SpendEnergyAsync, RefillEnergyAsync)
- New interface: IPlayerResourceRepository (GetAsync, AtomicUpdateAsync)
- New infra: PlayerResourceRepository — AtomicUpdateAsync uses PostgreSQL FOR UPDATE
- Live value computed from checkpoint + elapsed × regenPerMinute, capped at MaxValue
- SpendEnergy uses row-level lock; rejects if live value < amount after lock acquired
- All spends written to audit_log
- Tests: 7 unit tests covering regen, cap, spend, double-spend guard

System 6 — Player Profile (COMPLETE)
- GET /api/players/me returns full profile with live resource values; PUT /api/players/me updates username only
- Live values always computed via IEnergyService — stored checkpoint never returned directly
- PlayerId sourced from JWT on every call; soft-deleted players return 404 not 401
- Tests: 8 unit tests (live energy, update success/taken/notfound, validator accept/reject)

System 7 — Gem Ledger (COMPLETE)
- Append-only gem_transactions ledger; balance is always SUM(amount) — never a stored field
- Idempotency enforced via unique partial index on (player_id, transaction_type, reference_id)
- DailyRefill once-per-day enforced through referenceId = "daily:{yyyy-MM-dd}" — no extra timestamp column
- Tests: 5 unit tests (balance sum, duplicate ref rejection, insufficient balance, daily grant/reject)

System 8 — Quest System (COMPLETE)
- Static quest definitions from content/quests.json loaded once at startup; no DB table for definitions
- Quest attempt: energy spent first; if insufficient, returns failure with zero side effects guaranteed
- Gem rewards idempotent via referenceId = "quest:{questId}:{playerId}:{completionCount}"
- Tests: 7 unit tests (prereq filter, success, energy fail, prereq fail, bad id, level up, gem key)

System 9 — Combat / Raid Engine (BETA)
- GET /api/raids → list all active raids with caller's damage/hit stats; POST /api/raids/{id}/summon → 201/404; POST /api/raids/{id}/hit → 200/400/404/409/410/422
- Server-seeded RNG damage formula: base = (ATK×4 + DEF) × hitSize × RNG(0.85..1.15); hitSize ∈ {1,5,20}
- Redis idempotency cache (raidhit:{key}, 24h TTL): duplicate submissions return cached response with zero reprocessing
- Contribution tiers on kill: Legendary (top 3), Epic (top 10%), Rare (≥ minContributionForLoot damage), Participant
- Gem rewards idempotent via referenceId = "raid:{raidId}:{playerId}"; denormalized ParticipantCount for O(1) read
- Static raid definitions from content/raids.json (two bosses: raid_ironcolossus, raid_malachar)
- New entities: ActiveRaid, RaidParticipant; migration: AddRaidSystem
- Tests: 13 unit tests (summon, hit×3 sizes, damage formula, idempotency, expired, defeated, stamina fail, kill rewards, tiers, gem key, list filtering, damage tracking)

## Phase 1 BACKEND COMPLETE

## Documentation Index
- [Game Design & Unity UI Reference](docs/ui/ROTA_GameDesign_UI_Reference.md) — DotD mechanics analysis, screen-by-screen UI blueprints, Unity implementation prompt, content pipeline guide

## Run commands
docker-compose up -d                    ← start postgres + redis
dotnet build                            ← build check
dotnet test                             ← run all tests  
dotnet run --project src/ROTA.Api       ← run server
dotnet ef migrations add <Name> --project src/ROTA.Infrastructure --startup-project src/ROTA.Api
dotnet ef database update --project src/ROTA.Infrastructure --startup-project src/ROTA.Api