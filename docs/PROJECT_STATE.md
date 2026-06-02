# ROTA — Project State (current truth)

*Verified 2026-05-30 by file inventory + source tracing + green `dotnet build`/`dotnet test` runs.*
*Single source of current truth. `CLAUDE.md` = session history; `changelog.md` = release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred; C# SDK is v0.3.0). Clean Architecture: `src/ROTA.{Api,Application,Domain,
Infrastructure,Shared}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## Build status (High — run this session)
- **308 unit + 8 integration = 316 tests pass. 0 warnings, 0 errors.**
- `main` @ tag **v0.2.7-s4** (System 15 Slice 4 — legion combat integration). Prior slices tagged.
- Branch `v0.2.7-legion-s5-commander` ready to merge → **v0.2.7-s5**.

## Inventory (High)
10 controllers · 15 services · 19 entities · 19 enums · 18 repositories · 3 middleware ·
19 EF migrations (InitialCreate→AddCommanderGear) · 8 content JSON files · GitHub Actions CI.
(Slice 5 adds: PlayerCommanderGear entity, IPlayerCommanderGearRepository, PlayerCommanderGearRepository,
PlayerCommanderGearConfiguration, migration AddCommanderGear. ILegionService gains 3 methods.
RaidService gains 2 deps (IPlayerCommanderGearRepository, IGearDefinitionProvider).
RaidHitResponse gains CommanderProcFired/CommanderProcBonus.)

## Implemented & tested (High)
Auth · Rate limiting · Audit · Energy/resources · Player profile · Gem ledger · Quests+difficulty ·
Raid engine (pg advisory-lock, Redis idempotency) · Items/sigils · Stats · Class system ·
RBAC + beta keys + admin (REST+CLI) · Character gear (v0.2.4) · Conditional/stacking bonuses (v0.2.5) ·
Raid magic content layer (System 14 Slice 1 — MagicDefinition + IMagicDefinitionProvider + magics.json) ·
**Raid magic ownership (System 14 Slice 2 — PlayerMagic entity + GET /api/magics) ·
**Raid magic application (System 14 Slice 3 — RaidMagic + slot-cap advisory lock + world gate; DEEP review)** ·
**Raid magic damage procs (System 14 Slice 4 — DamageProc magic fires in HitRaidAsync; MagicProcBonus+MagicProcs in RaidHitResponse)** ·
**Raid magic utility effects (System 14 Slice 5 — CritChanceFlat/GoldProc/XpProc wired into HitRaidAsync; MagicCritBonus in RaidHitResponse)** ·
**Raid magic economy (System 14 Slice 6 — GrantMagicAsync idempotent upsert, BuyMagicAsync gem shop, magicDrops in loot tables wired into RaidService+QuestService)**.
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

## System 15 — Legion (v0.2.7, Slices 1–4 done)
- **Slice 1**: 6 enums, UnitDefinition/LegionDefinition models, IUnitDefinitionProvider/ILegionDefinitionProvider, content/units.json (8 units) + content/legions.json (3 legions). Tag v0.2.7-s1.
- **Slice 2**: PlayerUnit/PlayerLegion entities, EF configs, migration AddLegionOwnership, repos, LegionService (GetOwnedUnitsAsync/GetOwnedLegionsAsync), LegionController (GET /api/units, /api/legions). Tag v0.2.7-s2.
- **Slice 3**: PlayerLegionSlot entity, EF config, migration AddLegionSlots, PlayerLegionSlotRepository, full LegionService (SetActiveLegionAsync, AssignSlotAsync, ClearSlotAsync, ComputeLegionPowerAsync, GetLegionDetailAsync). Slot constraint validation (Race/Role/Attribute). Tag v0.2.7-s3.
- **Slice 4 (DEEP)**: LegionConfig (PowerScaling, UnitCoefficients, MaxUnitProcBonus), legion power integrated into HitRaidAsync preProc (same RNG multiplier+hitSize as charBase; inline from injected repos, NOT LegionService.ComputeLegionPowerAsync), unit-ability proc phase (separate cap from magic), RaidHitResponse gains LegionPower/UnitProcBonus/UnitProcs. Tag v0.2.7-s4.
- **Slice 5 (MODERATE)**: Commander slot — PlayerCommanderGear entity (one row per player, upsert in place), IPlayerCommanderGearRepository, EF config + migration AddCommanderGear. ILegionService: EquipCommanderAsync/UnequipCommanderAsync/GetCommanderAsync. LegionController: PUT/DELETE/GET /api/legions/commander. Combat: commander gear proc fires in proc phase off preProc (stats deliberately excluded — PlayerCommanderGear never reaches GetEffectiveCombatDataAsync path). RaidHitResponse: CommanderProcFired/CommanderProcBonus. Tag v0.2.7-s5.

## Not implemented (High)
Game client (C# SDK = v0.3.0) · discernment quest-drop-quality (later) ·
moderation (back-burnered) · world chat · guild · gauntlet · gacha/pity ·
equipment crafting / consumables · gear set bonuses (Phase 2) ·
structured log sink / monitoring · background jobs · Legion Slice 6 (economy).

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
