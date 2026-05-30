# ROTA — Project State (current truth)

*Verified 2026-05-29 by file inventory + source tracing + green `dotnet build`/`dotnet test` runs.*
*This file is the single source of current truth. `CLAUDE.md` is session history; `changelog.md` is the release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred). Clean Architecture: `src/ROTA.{Api,Application,Domain,Infrastructure,Shared}`,
tests in `tests/ROTA.{UnitTests,IntegrationTests}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## Build status (High — run this session)
- **193 tests pass: 186 unit + 7 integration. 0 warnings, 0 errors.**
- Branch `main` @ tag **v0.2.1**. **Unpushed — `main` is ~27 commits ahead of origin/main (no off-machine backup).**

## Inventory (High — globbed)
7 controllers · 11 application services · 12 domain entities · 10 enums · 11 repositories ·
3 middleware · 11 EF migrations (InitialCreate→AddBetaKeys) · 4 content JSON files ·
GitHub Actions CI (`.github/workflows/ci.yml`).

## Implemented & tested (High)
Auth (RS256, BCrypt12, refresh rotation, 3-session cap, Redis lockout) · Rate limiting
(per-IP/per-player, 429+Retry-After) · Audit logging · Energy/resources · Player profile ·
Gem ledger · Quests + difficulty · Raid engine (pg advisory-lock concurrency, Redis hit
idempotency, RaidSize Personal/Small/Large) · Items/sigils · Stat allocation · Class system ·
RBAC (`PlayerRoles` Flags) + single-use beta keys + admin seed + promote/demote (REST + CLI).

## Content state (High — Step 0 trace, 2026-05-29)
**Minimal but playable vertical slice:** 2 chapters, 5 quest nodes (3 battle + 2 boss: Iron
Colossus, Lord Malachar), 2 raids (both World tier), 12 items (2 materials, 2 stat bags, 8 sigils),
2 loot tables (4 difficulty tiers each). Quest boss kills drop sigils that summon the raids.
Not content-rich; sufficient for a demo loop.

## Partially implemented (High)
- SignalR registered (`AddSignalR()`) but **no hubs mapped** — real-time is inert.
- Energy regen uses stored per-row `RegenPerMinute`, **not** `ClassConfig`.
- Admin "panel" is API-only (no UI, by design).

## Not implemented (High)
Game client · moderation (spec only: `docs/specs/SYSTEM_13_MODERATION.md`) · world chat · guild ·
gauntlet · gacha/pity · equipment/crafting/consumables · structured log sink / monitoring ·
background jobs.

## Known issues / debt (High unless noted)
- **Reward steps are NOT atomic** (confirmed Step 0): quest energy spend commits in its own
  `FOR UPDATE` tx, then XP/gold/gems/loot write separately. Raid kill-rewards ARE wrapped in the
  advisory-lock tx, but the stamina spend sits outside it. Crash between spend and rewards loses
  the resource. Both carry `// Phase 2` notes. → needs a dedicated transactional fix.
- `EnergyService`→`ClassConfig` regen not wired.
- **Unpushed**: no remote backup.

## Resolved this session (was debt, now fixed)
- ✅ Web host auto-migrates in **Development** (production stays explicit) — fresh-DB seeder crash fixed.
- ✅ CI added (`dotnet build` + `dotnet test` on push/PR to main).
- ✅ EF enum-sentinel data corruption (Personal raids), CLI DI crash, beta-key burn, test hermeticity.
- ✅ Obsolete `UnitTest1.cs` removed; `Player.Create` de-duplicated.

## To verify (below High — do not treat as fact)
- Test coverage % (no coverage tool run).
- Exact lockout thresholds (live in `AuthLockoutService`, not traced).
- Production deployment topology (single vs multi-instance).

## Key docs
`docs/OPERATIONS.md` · `docs/ARCHITECTURE.md` · `docs/CURRENT_TASK.md` ·
`docs/DESIGN_NORTHSTAR.md` (aspirational vision, NOT current state).
