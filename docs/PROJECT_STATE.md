# ROTA — Project State (current truth)

*Verified 2026-05-30 by file inventory + source tracing + green `dotnet build`/`dotnet test` runs.*
*Single source of current truth. `CLAUDE.md` = session history; `changelog.md` = release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred; C# SDK is v0.3.0). Clean Architecture: `src/ROTA.{Api,Application,Domain,
Infrastructure,Shared}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## Build status (High — run this session)
- **252 unit + 8 integration = 260 tests pass. 0 warnings, 0 errors.**
- `main` @ tag **v0.2.6-s3** (System 14 Slice 3 — magic application/slots/world-gate). Prior slices tagged.

## Inventory (High)
9 controllers · 14 services · 16 entities · 15 enums · 14 repositories · 3 middleware ·
16 EF migrations (InitialCreate→AddRaidMagics) · 6 content JSON files · GitHub Actions CI.

## Implemented & tested (High)
Auth · Rate limiting · Audit · Energy/resources · Player profile · Gem ledger · Quests+difficulty ·
Raid engine (pg advisory-lock, Redis idempotency) · Items/sigils · Stats · Class system ·
RBAC + beta keys + admin (REST+CLI) · Character gear (v0.2.4) · Conditional/stacking bonuses (v0.2.5) ·
Raid magic content layer (System 14 Slice 1 — MagicDefinition + IMagicDefinitionProvider + magics.json) ·
**Raid magic ownership (System 14 Slice 2 — PlayerMagic entity + GET /api/magics) ·
**Raid magic application (System 14 Slice 3 — RaidMagic + slot-cap advisory lock + world gate; DEEP review)**.
- **Resource regen is class-based (v0.2.2):** energy/stamina/guild regen derive from `ClassConfig`
  (minutes-per-point). **GuildStamina now regenerates** (was 0). Stored `RegenPerMinute` is vestigial.
- **RaidSize set (v0.2.2):** Personal/Small/Medium/Large/Titanic, participant caps 1/10/25/50/250,
  enforced pre-spend on hit. Personal = summoner-only.
- **Raid on-hit rewards (v0.2.2):** XP = single 1–4 roll × stamina; gold = stamina × per-raid
  `goldPerStamina`; hit response now returns per-hit `XpGained`/`GoldGained`/`DamageDealt`.
- **Discernment crit (v0.2.3):** raid hits crit via `DiscernmentInvestment` — chance 5%→15% (+10%
  hard cap @1000 disc), damage 1.5×→2.5× (@5000 disc), tunable `CombatConfig`.
- **Character gear (v0.2.4):** 8 slots. Raid damage uses effective stats. Mount proc once per hit.
- **Conditional bonuses (v0.2.5):** JSON-only bonus framework. `ConditionalBonusEvaluator` shared
  by gear/future legions. 5 bonus types, 3 condition types. `FlatDamagePercent` applied after crit.
  Reward atomicity fixed: stamina spend inside advisory-lock tx (atomic rollback). `ProcBonus` is `long`.

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
structured log sink / monitoring · background jobs · legion system (next).

## Known issues / debt (High)
- (Resolved v0.2.5: reward atomicity — stamina spend now inside advisory-lock tx.)
- (Resolved v0.2.5: ProcBonus type — now `long`.)

## Balance values (accepted per CURRENT_TASK; tunable in appsettings)
- **Regen pacing — accepted:** Conscript = 5.0 min/point for BOTH energy and stamina (~2 h for full
  energy); higher classes regen faster (Eternal = 1.0). `ClassConfig.RegenMinutesPerPoint`. Offset by
  level-up RefillResource + future consumables. Revisit only if beta feedback shows it's too slow.
- GuildStamina regenerates at 2.0 min/point.

## To verify (below High)
Test coverage % · exact lockout thresholds · production deployment topology.

## Key docs
`docs/OPERATIONS.md` · `docs/ARCHITECTURE.md` · `docs/CURRENT_TASK.md` · `docs/DESIGN_NORTHSTAR.md`.
