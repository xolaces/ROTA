# ROTA — Project State (current truth)

*Verified 2026-05-29 by file inventory + source tracing + a green `dotnet build`/`dotnet test` run.*
*This file is the single source of current truth. `CLAUDE.md` is session history; `changelog.md` is the release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred). Clean Architecture: `src/ROTA.{Api,Application,Domain,Infrastructure,Shared}`,
tests in `tests/ROTA.{UnitTests,IntegrationTests}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## Build status (High — run this session)
- **193 tests pass: 186 unit + 7 integration. 0 warnings, 0 errors.**
- Branch `main` @ tag **v0.2.0**. **25 commits ahead of origin/main — NOT pushed (no off-machine backup).**

## Inventory (High — globbed)
7 controllers · 11 application services · 12 domain entities · 10 enums · 11 repositories ·
3 middleware · 11 EF migrations (InitialCreate→AddBetaKeys) · 4 content JSON files.

## Implemented & tested (High)
Auth (RS256, BCrypt12, refresh rotation, 3-session cap, Redis lockout) · Rate limiting
(per-IP/per-player, 429+Retry-After) · Audit logging · Energy/resources · Player profile ·
Gem ledger · Quests + difficulty · Raid engine (pg advisory-lock concurrency, Redis hit
idempotency, RaidSize Personal/Small/Large) · Items/sigils · Stat allocation · Class system ·
**System 12 = RBAC (`PlayerRoles` Flags) + single-use beta keys + admin seed + promote/demote
(REST + CLI)**.

## Partially implemented (High)
- SignalR registered (`AddSignalR()`) but **no hubs mapped** — real-time is inert.
- Energy regen uses stored per-row `RegenPerMinute`, **not** `ClassConfig`.
- Admin "panel" is API-only (no UI, by design).

## Not implemented (High)
CI/CD (no `.github`) · game client · moderation (spec only: `docs/specs/SYSTEM_13_MODERATION.md`)
· world chat · guild · gauntlet · gacha/pity · equipment/crafting/consumables · structured log
sink / monitoring · background jobs.

## Known issues / debt (High unless noted)
- **No web-host auto-migration**: startup seeder queries `players`; a fresh un-migrated DB crashes
  startup (`42P01`). Mitigation: run `dotnet ef database update` before `dotnet run` (see OPERATIONS.md).
- Reward steps not fully transactional (energy/stamina committed before quest/raid rewards) — *Medium, per CLAUDE.md, not re-traced*.
- `EnergyService`→`ClassConfig` regen not wired.
- Obsolete file `tests/ROTA.IntegrationTests/UnitTest1.cs` (1-line stub).
- 25 unpushed commits; no remote.

## To verify (below High — do not treat as fact)
- Quest content depth / playability of the core loop (`quests.json` not opened this audit).
- Test coverage % (no coverage tool run).
- Exact lockout thresholds (live in `AuthLockoutService`, not traced this audit).
- Production deployment topology (single vs multi-instance).

## Key docs
`docs/OPERATIONS.md` (commands/CLI/endpoints/secrets) · `docs/ARCHITECTURE.md` (enforced rules) ·
`docs/CURRENT_TASK.md` (next work) · `docs/DESIGN_NORTHSTAR.md` (aspirational vision, NOT current state).
