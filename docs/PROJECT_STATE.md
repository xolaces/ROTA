# ROTA — Project State (current truth)

*Verified 2026-05-30 by file inventory + source tracing + green `dotnet build`/`dotnet test` runs.*
*Single source of current truth. `CLAUDE.md` = session history; `changelog.md` = release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred; C# SDK is v0.3.0). Clean Architecture: `src/ROTA.{Api,Application,Domain,
Infrastructure,Shared}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## Build status (High — run this session)
- **219 unit + 7 integration = 226 tests pass. 0 warnings, 0 errors.**
- `main` @ tag **v0.2.4**, synced with origin (pushed).

## Inventory (High)
8 controllers · 12 services · 13 entities · 11 enums · 12 repositories · 3 middleware ·
14 EF migrations (InitialCreate→AddEquipmentSystem) · 5 content JSON files · GitHub Actions CI.

## Implemented & tested (High)
Auth · Rate limiting · Audit · Energy/resources · Player profile · Gem ledger · Quests+difficulty ·
Raid engine (pg advisory-lock, Redis idempotency) · Items/sigils · Stats · Class system ·
RBAC + beta keys + admin (REST+CLI) · **Character gear (v0.2.4)**.
- **Resource regen is class-based (v0.2.2):** energy/stamina/guild regen derive from `ClassConfig`
  (minutes-per-point). **GuildStamina now regenerates** (was 0). Stored `RegenPerMinute` is vestigial.
- **RaidSize set (v0.2.2):** Personal/Small/Medium/Large/Titanic, participant caps 1/10/25/50/250,
  enforced pre-spend on hit. Personal = summoner-only.
- **Raid on-hit rewards (v0.2.2):** XP = single 1–4 roll × stamina; gold = stamina × per-raid
  `goldPerStamina`; hit response now returns per-hit `XpGained`/`GoldGained`/`DamageDealt` (raid log).
- **Discernment crit (v0.2.3):** raid hits crit via `DiscernmentInvestment` — chance 5%→15% (+10%
  hard cap @1000 disc), damage 1.5×→2.5× (@5000 disc), tunable `CombatConfig`; response has
  `IsCrit`/`CritMultiplier`. Raids only (quests have no damage roll).
- **Character gear (v0.2.4):** 8 slots (Head/Neck/Torso/Ring1/Ring2/Mount/Boots/Gloves).
  `player_equipment` table (unique per slot per player). JSON-driven `gear.json`. Raid damage formula
  uses effective stats (base + gear ATK/DEF). Mount = proc: 5–40% chance → +procPercent×base damage,
  once per hit before crit. `PlayerStatsResponse`/`PlayerProfileResponse` expose effective stats.
  `RaidHitResponse` has `ProcFired`/`ProcBonus`. Starter set: 8 Grey pieces, full-set +1 ATK/+3 DEF,
  Draft Horse mount (5% proc ×200%). GET/PUT/DELETE `/api/equipment/{slot}`.

## Content state (High)
Minimal playable slice: 2 chapters, 5 quest nodes (3 battle + 2 boss), 2 raids, 12 items, 2 loot
tables. Loop works; thin.

## Partially implemented (High)
- SignalR registered, **no hubs mapped** (real-time inert).
- Admin "panel" is API-only (no UI).

## Not implemented (High)
Game client (C# SDK = v0.3.0) · discernment quest-drop-quality (later) ·
moderation (back-burnered) · world chat · guild · gauntlet · gacha/pity ·
equipment crafting / consumables · gear set bonuses (Phase 2) ·
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
