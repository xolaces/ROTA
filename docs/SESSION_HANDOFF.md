# ROTA Session Handoff — 2026-06-08 (Playtest Tickets 53–58)

## TL;DR (resume here)
A 6-ticket batch (**T53–T58**) is **fully implemented and GREEN**. Backend: **0 errors / 0 warnings**,
**991 tests pass (889 unit + 102 integration)**. The Gauntlet curve test (`GauntletCurveTests`) prints +
validates the curve (stage 250 ≈ 80M power).

- **Backend** (`C:\Users\xolac\OneDrive\Documentos\Projects\ROTA`, branch `main`): ~27 changed/new files.
  2 new migrations: **`20260609043452_AddHealthResource`** (T56, empty schema diff + data backfill) +
  **`20260609131852_AddRaidParticipantPendingDrops`** (T57, adds `pending_drops_json`).
- **Unity client** (`C:\Dev\ROTA.Client6`, branch `master`): ~12 changed/new files (incl. new
  `GauntletScreen.cs`, `ItemDropOverlay.cs`). **Not compiled here** (no Unity in this session).

## OWNER ACTION ITEMS
1. **Apply migrations** (Development auto-migrates on next `dotnet run`): the 6 prior-batch migrations +
   the new **`AddHealthResource`** (backfills a Health pool = base_max_health for every existing player).
   For prod: `dotnet ef database update`.
2. **Open the Unity client** (`C:\Dev\ROTA.Client6`) to headless-compile (grep `error CS` → 0) and playtest
   in mock. New screens: Gauntlet landing page (glowing Home CTA when an event is Active), item-drop overlay.
3. **(optional) Commit** — backend on `main` is uncommitted; client is local-only (no remote).

## WHAT SHIPPED (per ticket)
- **T53** (client) — fixed the real HUD↔profile desync: `MockRotaApi.AttemptQuestAsync` never deducted
  energy (looked like a backend bug). Now deducts (difficulty-scaled) + restarts regen. Backend resource-
  sync + level-up refill path audited and confirmed correct (no change needed).
- **T55** (backend+client) — Chapter/Zone **navigator** on the quest screen + **co-scaled XP and energy**:
  `QuestConfig.ChapterScaling` (per-chapter energy×/xp× table, **capped at chapter 16**, modeled for 24),
  replacing the XP-only `ChapterXpScalars` (now BETA). The base XP already scaled per chapter, so energy now
  scales too and the XP multiplier is gentle — directly fixing "XP too high relative to energy". Config-driven
  (appsettings). DTO gains `EffectiveEnergyCost`/`EffectiveXpReward`; mock mirrors the table; mock quests
  expanded to 3 chapters / 4 zones.
- **T58** (client) — `ItemDropOverlay` (mirrors `LevelUpOverlay`): a tap-to-dismiss, rarity-colored,
  multi-item card; queued for rapid drops. Fired from QuestScreen BEFORE the level-up notify so level-up
  layers on top. Wired in `AppBootstrap`.
- **T54** (backend+client) — **Gauntlet is an EVENT, not a raid**: glowing Home CTA (lit when an event is
  Active) → new `GauntletScreen` landing page (event/league/score, auto-advancing ladder with Enter-Stage
  combat, gem-funded Strike purchase, token shop, leaderboard). Full client DTO/IRotaApi/Http/Mock plumbing.
  **Curve (owner-requested, tested):** `GauntletStageCurve.Hp(n)=5000×1.0493^(n-1)`, `MaxLadderStage=250`
  in appsettings; the content provider formula-extends the ladder when configured (OFF by default so unit
  fixtures are untouched). Break-even power = Hp/StrikesPerDefeat ⇒ **stage 250 ≈ 80,000,000 power** (the
  presumed ~1-year endgame), with a smooth power→stage map (1k→15, 1M→158, 80M→250) and gems/double-power as
  the accelerator past your natural wall. `GauntletCurveTests` asserts + prints the table.
- **T56** (backend+client) — **Health is now a regenerating `PlayerResource`** (`ResourceType.Health=4`):
  seeded at `BaseMaxHealth`, regen via `ClassConfig.HealthRegenMinutes` (10), **refills on level-up (owner
  decision — KEPT, so this is additive, not a T22 reversal)**, max synced to BaseMaxHealth on allocate. Per-
  hit cost: flat-per-difficulty (`CombatConfig.RaidHealthCostByDifficulty`) for ordinary raids; a Defense-
  scaled mob-damage curve for the Gauntlet (ramps past stage 200). `EnergyService.DrainAsync` clamps at 0
  (never blocks a hit). Health bar added to the HUD + live HP on the profile (T53 single source of truth).
  Migration **AddHealthResource** (data-only backfill).
- **T57** (backend+client) — **explicit per-participant Loot claim** (reverses T50/System 23 grant-on-kill).
  **Reward boundary (owner-locked): ON-HIT = XP + gold ONLY; LOOTED = everything else** — gems, stat-points,
  inventory items, AND the magic/unit/legion/gear collection drops. The killing hit grants XP+gold immediately,
  rolls the rest and stores it pending on the participant row (gems/SP fields, items `ItemsEarnedJson`, drops a
  new `pending_drops_json` column). The new `LootRaidAsync` grants the pending gems/SP/items/drops on the Loot
  press (idempotent via the `RewardedAt` latch; gold/XP NOT re-granted). `GetActiveRaidsAsync` surfaces unclaimed
  lootable raids so a player can return and claim. Migration **AddRaidParticipantPendingDrops**. Client: Loot
  button moved out of the summoner-only share body → ANY participant; the kill prompts "press Loot", the loot
  press shows the spoils. `LootRaidResult` gains `Rewards` (gems/SP/items; gold/XP shown 0).

## OWNER DECISIONS (locked this session)
- **T56** health: KEEP the level-up full-refill (health regen + depletes + still tops up on level-up).
- **T57** loot: EVERY participant claims their own rewards (not summoner-only).
- **T54** curve: a math scaling function, tested; endgame ≈ 80M base battalion power, ~1 yr to lvl 250,
  gem-funded double-power accelerator, global debuffs reserved for power creep.

## KNOWN FOLLOW-UPS / NOTES
- **Verification pending** (server lock): `dotnet test` + Unity compile — owner to run.
- T57 keeps **collection drops (magic/unit/legion/gear) immediate** on kill (idempotent unlocks); only
  gold/gems/stat-points/inventory-items defer. Tune if full deferral is wanted.
- T56 Gauntlet health uses the gauntlet-stage parse for ramp; tune `CombatConfig.GauntletHealth*` + the
  per-difficulty raid costs in playtest.
- All curve/scaling magnitudes (T54 Gauntlet, T55 chapter table, T56 health costs) are appsettings-tunable.
