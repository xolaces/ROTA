# ROTA — Project State (current truth)

*Verified 2026-05-30 by file inventory + source tracing + green `dotnet build`/`dotnet test` runs.*
*Single source of current truth. `CLAUDE.md` = session history; `changelog.md` = release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred; C# SDK is v0.3.0). Clean Architecture: `src/ROTA.{Api,Application,Domain,
Infrastructure,Shared}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## Build status (High — run this session)
- **321 unit + 9 integration = 330 tests pass. 0 warnings, 0 errors.**
- `main` past tag **v0.2.7-s6** (Legion epic complete) + 3 post-fixes merged & pushed (untagged hardening):
  gem-buy lost-purchase recovery, class-based regen DTO field, System 16 Gauntlet **draft** spec.

## Inventory (High)
10 controllers · 15 services · 19 entities · 22 enums · 18 repositories · 3 middleware ·
19 EF migrations (InitialCreate→AddCommanderGear) · 8 content JSON files · GitHub Actions CI.
(Slice 6 adds: GemPrice to UnitDefinition/LegionDefinition, GemTransactionType.UnitPurchase/LegionPurchase,
UnitDropChance/LegionDropChance loot drop models, GrantUnitAsync/GrantLegionAsync/BuyUnitAsync/BuyLegionAsync
in ILegionService+LegionService. QuestService+RaidService wired to fire unit/legion drops from loot tables.
Two auditor fold-ins: AssignSlotAsync quantity > 0 guard; ComputeLegionPowerAsync reads LegionConfig coefficients.)

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

## System 15 — Legion (v0.2.7, Slices 1–6 — COMPLETE)
- **Slice 1**: 6 enums, UnitDefinition/LegionDefinition models, IUnitDefinitionProvider/ILegionDefinitionProvider, content/units.json (8 units) + content/legions.json (3 legions). Tag v0.2.7-s1.
- **Slice 2**: PlayerUnit/PlayerLegion entities, EF configs, migration AddLegionOwnership, repos, LegionService (GetOwnedUnitsAsync/GetOwnedLegionsAsync), LegionController (GET /api/units, /api/legions). Tag v0.2.7-s2.
- **Slice 3**: PlayerLegionSlot entity, EF config, migration AddLegionSlots, PlayerLegionSlotRepository, full LegionService (SetActiveLegionAsync, AssignSlotAsync, ClearSlotAsync, ComputeLegionPowerAsync, GetLegionDetailAsync). Slot constraint validation (Race/Role/Attribute). Tag v0.2.7-s3.
- **Slice 4 (DEEP)**: LegionConfig (PowerScaling, UnitCoefficients, MaxUnitProcBonus), legion power integrated into HitRaidAsync preProc (same RNG multiplier+hitSize as charBase; inline from injected repos, NOT LegionService.ComputeLegionPowerAsync), unit-ability proc phase (separate cap from magic), RaidHitResponse gains LegionPower/UnitProcBonus/UnitProcs. Tag v0.2.7-s4.
- **Slice 5 (MODERATE)**: Commander slot — PlayerCommanderGear entity (one row per player, upsert in place), IPlayerCommanderGearRepository, EF config + migration AddCommanderGear. ILegionService: EquipCommanderAsync/UnequipCommanderAsync/GetCommanderAsync. LegionController: PUT/DELETE/GET /api/legions/commander. Combat: commander gear proc fires in proc phase off preProc (stats deliberately excluded — PlayerCommanderGear never reaches GetEffectiveCombatDataAsync path). RaidHitResponse: CommanderProcFired/CommanderProcBonus. Tag v0.2.7-s5.
- **Slice 6 (MODERATE)**: Economy/acquisition — GemPrice on unit/legion defs. GrantUnitAsync/GrantLegionAsync (idempotent upsert). BuyUnitAsync/BuyLegionAsync: ownership pre-check → AlreadyOwned 409 without charge, idempotent referenceId (unitbuy:{playerId}:{id} / legionbuy:{…}). GemTransactionType.UnitPurchase/LegionPurchase. LootTable: UnitDropChance/LegionDropChance added to ThresholdReward (raid) and LootTableDifficulty (quest); wired in RaidService.DistributeKillRewardsAsync and QuestService.ApplyLootAsync. Auditor fold-ins: AssignSlotAsync Quantity>0 guard; ComputeLegionPowerAsync reads LegionConfig.UnitCoefficients (no hardcoded values). LegionService gains IGemService + IOptions<LegionConfig>; QuestService+RaidService gain ILegionService dep. POST /api/units/buy + POST /api/legions/buy. Tag v0.2.7-s6.

## Not implemented (High)
Game client (C# SDK = v0.3.0) · discernment quest-drop-quality (later) ·
moderation (back-burnered) · world chat · guild · gauntlet · gacha/pity ·
equipment crafting / consumables · gear set bonuses (Phase 2) ·
structured log sink / monitoring · background jobs.

## Known issues / debt (High)
- (Resolved v0.2.5: reward atomicity — stamina spend now inside advisory-lock tx.)
- (Resolved v0.2.5: ProcBonus type — now `long`.)
- **(Resolved 2026-06-02: gem-buy lost-purchase recovery.)** `GemService.SpendGemsAsync` now returns a
  tri-state `GemSpendOutcome` (Charged / AlreadyProcessed / InsufficientBalance). All 3 shops
  (magic/unit/legion) treat AlreadyProcessed as success and re-run the idempotent grant, so a
  charged-but-not-granted retry recovers the item with no double-charge. A real Testcontainers integration
  test (`BuyUnitIdempotencyTests`) proves single-charge + re-grant against the live ledger + unique index.
  **Still PHASE-2:** wrapping spend+grant in one DB transaction (needs a cross-repo transaction-scope
  abstraction) — the recovery path makes this a hardening step, not a correctness fix; `// PHASE-2` notes
  are in all 3 buy methods.
- (Documented Phase-2, pre-existing: `GemService` concurrent balance-overspend — non-atomic balance check
  + insert can drive balance negative under contention across *different* referenceIds; advisory lock deferred.)

## Balance values (accepted per CURRENT_TASK; tunable in appsettings)
- **Regen pacing — accepted:** Conscript = 5.0 min/point for BOTH energy and stamina (~2 h for full
  energy); higher classes regen faster (Eternal = 1.0). `ClassConfig.RegenMinutesPerPoint`. Offset by
  level-up RefillResource + future consumables. Revisit only if beta feedback shows it's too slow.
- GuildStamina regenerates at 2.0 min/point.

## To verify (below High)
Test coverage % · exact lockout thresholds · production deployment topology.

## Key docs
`docs/OPERATIONS.md` · `docs/ARCHITECTURE.md` · `docs/CURRENT_TASK.md` · `docs/DESIGN_NORTHSTAR.md`.
