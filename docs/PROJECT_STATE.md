# ROTA — Project State (current truth)

*Verified 2026-05-30 by file inventory + source tracing + green `dotnet build`/`dotnet test` runs.*
*Single source of current truth. `CLAUDE.md` = session history; `changelog.md` = release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred; C# SDK is v0.3.0). Clean Architecture: `src/ROTA.{Api,Application,Domain,
Infrastructure,Shared}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## Build status (High — run this session)
- **207 tests pass: 200 unit + 7 integration. 0 warnings, 0 errors.**
- `main` @ tag **v0.2.2**. (Pushing to origin this session — see CURRENT_TASK if backup status matters.)

## Inventory (High)
7 controllers · 11 services · 12 entities · 10 enums · 11 repositories · 3 middleware ·
13 EF migrations (InitialCreate→FixRaidSizeSentinel) · 4 content JSON files · GitHub Actions CI.

## Implemented & tested (High)
Auth · Rate limiting · Audit · Energy/resources · Player profile · Gem ledger · Quests+difficulty ·
Raid engine (pg advisory-lock, Redis idempotency) · Items/sigils · Stats · Class system ·
RBAC + beta keys + admin (REST+CLI).
- **Resource regen is class-based (v0.2.2):** energy/stamina/guild regen derive from `ClassConfig`
  (minutes-per-point). **GuildStamina now regenerates** (was 0). Stored `RegenPerMinute` is vestigial.
- **RaidSize set (v0.2.2):** Personal/Small/Medium/Large/Titanic, participant caps 1/10/25/50/250,
  enforced pre-spend on hit. Personal = summoner-only.
- **Raid on-hit rewards (v0.2.2):** XP = single 1–4 roll × stamina; gold = stamina × per-raid
  `goldPerStamina`; hit response now returns per-hit `XpGained`/`GoldGained`/`DamageDealt` (raid log).

## Content state (High)
Minimal playable slice: 2 chapters, 5 quest nodes (3 battle + 2 boss), 2 raids, 12 items, 2 loot
tables. Loop works; thin.

## Partially implemented (High)
- SignalR registered, **no hubs mapped** (real-time inert).
- Admin "panel" is API-only (no UI).

## Not implemented (High)
Game client (C# SDK = v0.3.0) · discernment crit (v0.2.3) · discernment quest-drop-quality (later) ·
moderation (back-burnered) · world chat · guild · gauntlet · gacha/pity · equipment/crafting ·
structured log sink / monitoring · background jobs.

## Known issues / debt (High)
- **Reward atomicity:** the raid **stamina spend** still runs in its own tx, outside the advisory-lock
  block; on-hit XP/gold and kill rewards ARE inside it. Crash between stamina-spend and the lock block
  loses stamina. Documented `// Phase 2`.
- (Resolved this session: class regen wiring, raid size set, raid on-hit rewards, CI, dev auto-migrate.)

## Needs owner sign-off (balance values, tunable in appsettings)
- **Regen pacing:** Conscript = 5.0 min/point for BOTH energy and stamina (≈10× slower than the old
  dev placeholder; ~2 h for full energy). Higher classes regen faster (Eternal = 1.0). Confirm these
  `ClassConfig.RegenMinutesPerPoint` values are the intended pacing.
- GuildStamina now regenerates at 2.0 min/point.

## To verify (below High)
Test coverage % · exact lockout thresholds · production deployment topology.

## Key docs
`docs/OPERATIONS.md` · `docs/ARCHITECTURE.md` · `docs/CURRENT_TASK.md` · `docs/DESIGN_NORTHSTAR.md`.
