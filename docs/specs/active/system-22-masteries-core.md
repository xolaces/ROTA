# System 22 — Phase A: Masteries Core (spec + sliced task queue)

*Spec locked 2026-06-07. Phase A of the System 22 design (`docs/specs/backlog/system-22-ancients-rise-and-masteries.md`,
Parts A1–A6 + "Phase A" row + Architecture hooks + Resolved decisions). **Scope is Phase A ONLY** — Part B
(The Rise / Ancient raid) and Part C (PoE-depth: pick-one-of-N nodes, stat-tree, gear enchants) are OUT OF
SCOPE. Build against this exactly. Follows the System 15 (Legion) slice discipline: one slice per branch,
build 0 warnings + tests green, commit/merge independently, never bundle. Every reuse hook is anchored to a
real `file:line` (verified against the current tree on `feat/system21-guild-s3b-guild-raids`).*

---

## Core insight

Masteries are **small permanent bonus *modifiers*, never mechanic-changers** — and every one of the four
effects already has a live insertion point in the combat/loot pipeline. There is **no new combat path** and
**no new loot path**: a mastery is a per-player level (1→5) per Ancient, and the bonus is a plain number
folded into an existing hook. The modifier values are read from a dedicated `IMasteryService`; they are
**not** modelled as `ConditionalBonus` rows (see Locked Decision #2 for why). This mirrors how the Gauntlet
trophy multiplier and off-cap auras were added — per-player DB-state buffs applied as direct conditional adds
at the existing hook, not as content bonuses.

The four hooks (all confirmed in source):

| Mastery | Effect | Reuse hook (file:line) |
|---|---|---|
| **Wrath** | +% Legion power (never Gauntlet) | additive term in `totalLegionBonus` before `RaidService.cs:701` |
| **Bulwark** | +% guild-raid damage (~1% hard cap) | `FlatDamagePercent` add at `RaidService.cs:909-911`, gated `lockedRaid.GuildId != null` |
| **Hoard** | +% drop rate / gold | quest `Scale` closure `QuestService.cs:363-368`; gold `QuestService.cs:192` / `RaidService.cs:977,1220,1343`; raid threshold drops `RaidService.cs:1273+` |
| **Discernment** | +% drop quality (rarity-upgrade) + sigil find | sigil find `QuestService.cs:284`; drop-quality = NEW opt-in `upgradesTo` content field (Slice 7) |

The progression spine is a per-Ancient **challenge checklist** of deterministic activity counters, incremented
best-effort at existing activity chokepoints (mirroring the System 17 leaderboard `RecordRaidHitAsync` hook).

---

## Locked design decisions (owner-confirmed in the design doc + implementation decisions locked here)

### From the design doc (do not redesign)
1. **Four Ancients, all pledgeable from launch:** Wrath, Bulwark, Hoard, Discernment. Each level 1→5.
2. **Two stacked components per mastery:** an always-on **Global** buff that scales with that mastery's level,
   plus a **Pledge** bonus on the ONE active mastery that ≈ **doubles** that Ancient's bonus. Modifiers only.
3. **Magnitudes (TUNE dials, shape locked):** Global @ L5 / pledged @ L5 ≈ Wrath +2.5% / ~5%; Bulwark +0.5% /
   **~1% hard cap** (direct combat %, the deliberate exception); Hoard +4% / ~8%; Discernment +4% / ~8%.
4. **Wrath touches Legion power only — NEVER the Gauntlet.** Gated on having an active legion; folded into the
   legion bonus fraction, which the Gauntlet trophy/aura amplifiers sit *after* (so Wrath never double-touches).
5. **Bulwark applies to guild raids only** (`ActiveRaid.GuildId != null`), hard-capped.
6. **Overall Mastery Rating = Formula B** (Σ levels + breadth thresholds + depth bonus). Active (live) vs
   Lifetime (high-water) split. Gates titles + QoL + a small capped micro-bonus — NEVER raw competitive power.
7. **Re-spec is LOSSLESS:** free monthly + paid weekly (gems) + free on each new-Ancient unlock; only changes
   which mastery is *pledged*; all four leveled tracks stay banked.
8. **Leveling 1→5 via per-Ancient challenge checklists** (deterministic counters, breadth curve, ~3–6 months).
9. **Pick-one-of-N nodes / stat-tree / enchants are Phase C — NOT built here.**

### Implementation decisions locked here (resolved from the codebase mapping)
10. **Masteries are a dedicated `IMasteryService`, NOT `ConditionalBonus` rows.** The `ConditionalBonus` engine
    (`ConditionalBonusEvaluator`) is a pure, I/O-free evaluator over **inventory/equipped-slot state** carried
    on **content defs** (gear/magic/unit). Masteries are **per-player DB level-state**; 3 of the 4 effects
    (Wrath→legion power, Hoard/Discernment→loot) never pass through `EffectiveCombatData` at all, and Bulwark
    needs a raid-context gate (`GuildId`) the evaluator cannot express. So masteries follow the **trophy/aura
    precedent**: a service returns plain numbers consumed at each existing hook. (Phase C node/enchant bonuses
    *will* use `ConditionalBonus` — that is correct, but it is out of scope.)
11. **Pledge = doubling via a single `pledgeMultiplier` (default 2.0).** Content carries one
    `globalPercentByLevel[5]` table per Ancient; the **global** value at the player's level applies always; the
    **pledged** Ancient gets `global × pledgeMultiplier` (Bulwark clamped to its hard cap). One number per
    Ancient per level, not two.
12. **Overall Rating uses Formula B *without* the optional weakest-pillar floor by default** (matches the
    design's worked examples exactly: (5,1,1,1)=10, (3,3,3,2)=14, (5,5,5,5)=56). The floor is a config-gated
    optional term, default OFF.
13. **Active Rating == Lifetime Rating in Phase A.** Mastery levels are **monotonic** (leveling only goes up;
    re-spec is lossless and never lowers a level), so the two coincide. Both are surfaced (design vocabulary +
    forward-compat for a future seasonal mechanic) but computed from the same current levels; no separate
    high-water storage is built in Phase A.
14. **Titles are DERIVED strings, not a stored entity.** No title/cosmetic system exists in the codebase. Titles
    are a pure function of the 4 levels (breadth thresholds + `Master of <Ancient>` per maxed Ancient +
    `Ascendant of the Ancients` at all-≥5), computed on read and surfaced as strings (reusing the
    `ChatMessageDto.SenderRole` "carry a string, client renders" pattern). No `player_titles` table.
15. **Re-spec via a dedicated endpoint + dedicated ledger, NO "Bazaar" abstraction.** There is no server-side
    Bazaar/SKU catalogue (shops are per-system). The paid re-spec is a direct gem sink modelled on
    `MagicService.BuyMagicAsync`: `POST /api/masteries/pledge` → `MasteryService.RespecAsync`. The weekly cap
    uses the **Redis INCR+TTL** idiom (`AuthLockoutService` pattern, per the design's "weekly cap in Redis");
    the gem spend is idempotent via `IGemService.SpendGemsAsync(MasteryRespec, refId)`; a dedicated append-only
    `mastery_respec_transactions` ledger records all three swap kinds (audit + monthly/unlock idempotency).
    `GemTransactionType.MasteryRespec = 13` (next free int).
16. **Active pledge is a denormalized nullable field on `Player`** (`ActivePledgeAncient`), mirroring the
    `Player.GuildId`/`GuildRank` convention — written only by `MasteryService`.
17. **Discernment "drop quality" = a new opt-in `upgradesTo` content field** (Slice 7, independently cuttable).
    No rarity-upgrade mechanism exists today. We add an optional per-item/gear `upgradesTo` pointer (the
    next-tier-up def id; null = never upgrades). A Discernment-scaled roll at drop-resolution substitutes the
    upgrade, clamped to `ItemRarity.Orange`. This avoids building a global item-family system. **See "Open items
    for owner confirmation" — this slice can be deferred without affecting Slices 1–6.**
18. **Combat/loot modifiers read the last-persisted `PlayerMastery.Level`** (cheap indexed read of ≤4 rows per
    hit, like the trophy query). **Tier-up is evaluated off the hot path** (on `GET /api/masteries` reads and
    after quest completion), so the combat money-path only does counter increments + a level read — never tier
    evaluation. A mastery tier-up taking effect on the next quest/profile-load rather than mid-raid is accepted.

**Deferred (document only, do NOT build):** The Rise / `AncientEvent` / Ancient raid (Part B); pick-one-of-N
per-tier nodes, per-Ancient stat-tree, gear enchantments, Discernment literal discovery layer, seasonal layer
(Part C); the scheduled auto-driver for any background mastery recompute (manual CLI + on-read is enough).

---

## Magnitude & formula reference (TUNE values locked as the starting point)

### Per-Ancient global magnitude tables (`content/masteries.json`, `globalPercentByLevel`)

All values are **percent** (e.g. `2.5` = +2.5%). Global applies always at the player's level; pledged Ancient =
`global × pledgeMultiplier` (default 2.0), Bulwark clamped to `MasteryConfig.BulwarkMaxGuildDamagePercent`.

| Ancient | L1 | L2 | L3 | L4 | L5 (global) | L5 pledged (×2) |
|---|---|---|---|---|---|---|
| **Wrath** (legion power %) | 0.5 | 1.0 | 1.5 | 2.0 | **2.5** | ~5.0 |
| **Bulwark** (guild-raid dmg %) | 0.1 | 0.2 | 0.3 | 0.4 | **0.5** | ~1.0 (hard cap) |
| **Hoard** (drop rate + gold %) | 0.8 | 1.6 | 2.4 | 3.2 | **4.0** | ~8.0 |
| **Discernment** (quality + sigil find %) | 0.8 | 1.6 | 2.4 | 3.2 | **4.0** | ~8.0 |

Conversion to a fraction at a hook: `fraction = percent / 100.0`. Wrath enters `totalLegionBonus` as a *percent*
(it is summed pre-`/100.0`, exactly like `LegionDefinition.PowerBonus`). Bulwark/Hoard/Discernment enter their
hooks as *fractions*.

### Overall Mastery Rating — Formula B (pure function over the four levels `Mᵢ ∈ 1..5`)

```
Rating = Σ Mᵢ
       + 3   if all four ≥ 2
       + 5   if all four ≥ 3
       + 8   if all four ≥ 4
       + 12  if all four ≥ 5
       + 2 × (count of Mᵢ == 5)                 // depth bonus
       [+ 2 × min(Mᵢ)  IF MasteryConfig.IncludeWeakestPillarFloor == true ]   // default OFF
```

Worked (must be asserted by unit tests): (5,1,1,1)=10 · (3,3,3,2)=14 · (1,1,1,1)=4 · (2,2,2,2)=11 · (5,5,5,5)=56.

### Titles (derived; surfaced as strings)

| Title | Condition |
|---|---|
| `Touched Everything` | all four ≥ 2 |
| `Well-Rounded` | all four ≥ 3 |
| `Paragon of the Ancients` | all four ≥ 4 |
| `Ascendant of the Ancients` | all four ≥ 5 (the jackpot) |
| `Master of Wrath/Bulwark/Hoard/Discernment` | that Ancient at L5 (one per maxed Ancient) |

The DTO returns the highest breadth title + the list of `Master of …` titles. Client renders.

### Capped micro-bonus (breadth reward, NEVER raw competitive power — TUNE)

A tiny, hard-capped horizontal bonus earned by breadth, routed through the **Hoard drop/gold lane** (so it stays
horizontal and inside the global cap). `MasteryConfig.BreadthMicroBonusPercent` (default `0.0` → effectively
off until tuned) added to the Hoard drop/gold fraction when the player has reached a breadth threshold (all ≥ 3),
capped at `MasteryConfig.BreadthMicroBonusMaxPercent` (default `2.0`). Flagged for owner sizing.

---

## Data model (whole of Phase A)

### Enums (`src/ROTA.Domain/Enums/`)

- **`MasteryAncient`** `{ Wrath = 0, Bulwark = 1, Hoard = 2, Discernment = 3 }` — the four pledgeable Ancients.
- **`MasteryActivityType`** — deterministic activity counters fed by the chokepoints (Slice 4):
  `{ RaidHit = 0, RaidDamageDealt = 1, RaidKill = 2, QuestNodeCleared = 3, QuestBossCleared = 4,
     GuildRaidContribution = 5, GauntletRankEarned = 6, LevelGained = 7, EnergySpent = 8, StaminaSpent = 9,
     GoldEarned = 10 }`. Append-only; never renumber (persisted as int).
- **`MasteryRespecKind`** `{ Paid = 0, FreeMonthly = 1, NewAncientUnlock = 2 }` — the respec-ledger discriminator.
- **`MasteryRespecFailureCode`** `{ None, AncientNotFound, AlreadyPledged, WeeklyCapReached, InsufficientGems,
     PlayerNotFound }`.
- **Additive enum value:** `GemTransactionType.MasteryRespec = 13` (code-only; stored as int, no migration/sentinel).
- **Additive enum values:** `LeaderboardBoard.MasteryRatingActive = 6`, `MasteryRatingLifetime = 7` (append at end).

### Config — `MasteryConfig` (`src/ROTA.Application/Configuration/MasteryConfig.cs`, `IOptions`, bound from `"MasteryConfig"`)

```
PledgeMultiplier                  double  default 2.0    (pledged Ancient = global × this)
BulwarkMaxGuildDamagePercent      double  default 1.0    (hard cap on Bulwark's guild-raid dmg %, pledged or not)
RespecGemCost                     int     default 150    (TUNE — flat paid-swap gem price; mirror GauntletConfig.StrikeGemPrice)
IncludeWeakestPillarFloor         bool    default false  (Formula B optional term)
BreadthMicroBonusPercent          double  default 0.0    (capped breadth micro-bonus, off until tuned)
BreadthMicroBonusMaxPercent       double  default 2.0    (hard cap on the micro-bonus)
SigilFindAppliesToFirstClear      bool    default false  (guaranteed first-clear sigil never scaled; locked false)
```

Combat/loot magnitudes themselves live in `content/masteries.json` (content-shaped per-Ancient-per-level table);
`MasteryConfig` holds only scalar dials. POCO defaults mirror appsettings so the app boots if the section is absent.

### Content models (`src/ROTA.Application/Models/`, JSON in `content/masteries.json`)

**`AncientDefinition`**
```
ancient (MasteryAncient), name (string), theme (string), description (string),
globalPercentByLevel (double[5]),               // index 0 = L1 … index 4 = L5
tierChallenges (MasteryTierChallenge[4]),       // T1→2, T2→3, T3→4, T4→5  (index 0 = advance from L1 to L2)
iconKey (string)
```

**`MasteryTierChallenge`** = `{ fromLevel (int 1..4), checklist (MasteryChallengeItem[]) }`. Advance when **ALL**
checklist items are met.

**`MasteryChallengeItem`** = `{ activityType (MasteryActivityType), threshold (long) }`.

**`upgradesTo` (Slice 7, additive):** new optional `string? UpgradesTo` field on `ItemDefinition` and
`GearDefinition` — the def id of the same-slot, strictly-higher-rarity item this can upgrade into (null = never).

### Content (`src/ROTA.Api/content/masteries.json`) — starter checklists (TUNE numbers; breadth curve)

Late tiers (T3→4, **T4→5**) require **cross-system** counters; early tiers fall out of normal play.

| Ancient | T1→2 | T2→3 | T3→4 | T4→5 (cross-system) |
|---|---|---|---|---|
| **Wrath** | RaidHit 100 | RaidDamageDealt 5,000,000 | RaidKill 25 + RaidDamageDealt 50,000,000 | RaidKill 100 + GauntletRankEarned 3 + GuildRaidContribution 20,000,000 |
| **Bulwark** | GuildRaidContribution 1,000,000 | GuildRaidContribution 10,000,000 | GuildRaidContribution 50,000,000 + RaidKill 25 | GuildRaidContribution 150,000,000 + QuestBossCleared 20 + RaidKill 100 |
| **Hoard** | QuestNodeCleared 50 | GoldEarned 1,000,000 | QuestNodeCleared 320 + QuestBossCleared 10 | QuestNodeCleared 1000 + GoldEarned 25,000,000 + GauntletRankEarned 3 |
| **Discernment** | QuestNodeCleared 50 | RaidKill 10 + QuestBossCleared 5 | RaidKill 30 + QuestBossCleared 15 + GuildRaidContribution 5,000,000 | RaidKill 75 + QuestBossCleared 30 + GuildRaidContribution 30,000,000 + GauntletRankEarned 3 |

### Entities (`src/ROTA.Domain/Entities/`) — private setters, no EF attributes, snake_case, `id`/`created_at`/`updated_at`/`is_deleted`

- **`PlayerMastery`** — `Id, PlayerId, Ancient (MasteryAncient), Level (int, 1..5), created/updated/IsDeleted`.
  `Create(playerId, ancient)` → Level 1. `LevelUp()` → `Level = Math.Min(5, Level+1); Touch()`. `SetLevel(int)`
  (guarded 1..5, monotonic — throws on decrease). Unique `(player_id, ancient)` (one row per Ancient per player;
  lazy-created at L1 on first read/activity).
- **`PlayerMasteryActivity`** — `Id, PlayerId, ActivityType (MasteryActivityType), Counter (long), created/updated/IsDeleted`.
  `Create(playerId, activityType, initial)`, `Add(long delta)`. Unique `(player_id, activity_type)`. Atomic
  increment via `INSERT … ON CONFLICT (player_id, activity_type) DO UPDATE SET counter = counter + EXCLUDED.counter`
  (the leaderboard `IncrementAsync` pattern).
- **`MasteryActivityEvent`** — **append-only idempotency ledger** for exactly-once seams only —
  `Id, PlayerId, ActivityType, ReferenceId, CreatedAt`. Unique partial `(player_id, activity_type, reference_id)
  WHERE reference_id IS NOT NULL`. Written before the aggregate increment for referenced activities (gauntlet
  rank, per-level level-up); a unique-violation → already processed → skip the increment.
- **`MasteryRespecTransaction`** — **append-only ledger** —
  `Id, PlayerId, Kind (MasteryRespecKind), FromAncient (MasteryAncient?), ToAncient (MasteryAncient),
   GemCost (int, 0 for free swaps), ReferenceId (string), CreatedAt`. Unique `(player_id, reference_id)`. RefId
  schemes: paid `respec:paid:{playerId}:{isoYear}-W{ww}`; free-monthly `respec:free:{playerId}:{yyyy-MM}`;
  unlock `respec:unlock:{playerId}:{ancient}`.
- **`Player`** additive: `MasteryAncient? ActivePledgeAncient` (denorm) + `SetPledge(MasteryAncient)` /
  `ClearPledge()` (MasteryService sole writer; mirrors `JoinGuild`/`LeaveGuild`). Migration adds the column.

### Migrations (created, NOT applied — owner runs `database update`)
- **`AddMasterySystem`** (Slice 2): `player_masteries`, `player_mastery_activities`, `mastery_activity_events`
  tables + `players.active_pledge_ancient` column.
- **`AddMasteryRespecLedger`** (Slice 3): `mastery_respec_transactions` table.
- (Slice 7) **`AddItemGearUpgradesTo`**: `upgrades_to` column on the relevant content-backed tables — **none**;
  `UpgradesTo` lives only on the JSON content models (`ItemDefinition`/`GearDefinition`), which are not DB
  entities. **No migration needed for Slice 7.**

### Repositories (`Application/Interfaces` + `Infrastructure/.../Repositories/`, scoped)
- **`IPlayerMasteryRepository`** — `GetForPlayerAsync`, `FindAsync(playerId, ancient)`, `UpsertAsync`,
  `EnsureAllAsync(playerId)` (lazy-create the 4 L1 rows if absent).
- **`IPlayerMasteryActivityRepository`** — `GetForPlayerAsync`, `IncrementAsync(playerId, activityType, delta)`
  (raw-Npgsql ON CONFLICT, ambient-tx aware — clone `LeaderboardEntryRepository.IncrementAsync`),
  `TryRecordEventAsync(playerId, activityType, referenceId)` (idempotency-ledger insert; false if already present).
- **`IMasteryRespecRepository`** — `ReferenceExistsAsync(playerId, referenceId)`, `CreateAsync`,
  `CountByPrefixAsync(playerId, prefix)` (free-monthly / unlock existence checks; date-bucket pattern).

### Services
- **`IMasteryService`** + `MasteryService` (`Application/Interfaces` + `Application/Services`):
  - `GetMasteriesAsync(playerId)` → `MasteryOverviewResponse` (lazy-ensures L1 rows; evaluates tier-up;
    computes rating/titles/checklist-progress/modifier preview).
  - `GetCombatModifiersAsync(playerId)` → `MasteryCombatModifiers` (Wrath legion %, Bulwark guild-raid %).
  - `GetLootModifiersAsync(playerId)` → `MasteryLootModifiers` (Hoard drop mult, Hoard gold mult, Discernment
    quality mult, Discernment sigil-find mult).
  - `RespecAsync(playerId, toAncient)` → `MasteryRespecResult` (eligibility resolution + lossless swap).
  - `GrantUnlockRespecAsync(playerId, ancient)` → seeds a NewAncientUnlock free-swap entitlement (called by the
    first-pledge path now; Phase B's Rise will call it when a new Ancient awakens).
  - `RecordActivityAsync(playerId, activityType, amount = 1, referenceId = null)` → counter increment (best-effort
    at chokepoints; idempotent when referenceId supplied).
  - `int ComputeRating(IReadOnlyDictionary<MasteryAncient,int> levels)` (pure Formula B; static-testable).
  - `SnapshotRatingBoardAsync()` → populates the MasteryRating leaderboard board (Live snapshot pattern).
- Combat magnitude resolution is a pure helper: `MasteryCombatModifiers/LootModifiers` built from the 4 levels +
  pledge + `AncientDefinition.globalPercentByLevel` + `MasteryConfig`.

### DTOs (`src/ROTA.Shared/DTOs/MasteryDTOs.cs`)
`MasteryOverviewResponse` { `Ancients` (`MasteryAncientDto[]`), `ActivePledge` (string?), `Rating`
(`MasteryRatingDto`), `Titles` (`MasteryTitlesDto`), `RespecStatus` (`MasteryRespecStatusDto`) }.
`MasteryAncientDto` { `Ancient`, `Name`, `Theme`, `Level`, `IsPledged`, `GlobalPercent`, `EffectivePercent`
(global or pledged), `NextTier` (`MasteryTierProgressDto?` — null at L5) }.
`MasteryTierProgressDto` { `FromLevel`, `ToLevel`, `Items` (`{ ActivityType, Current, Threshold }[]`),
`Complete` (bool) }.
`MasteryRatingDto` { `Active`, `Lifetime` }. `MasteryTitlesDto` { `Breadth` (string?), `Masteries` (string[]) }.
`MasteryRespecStatusDto` { `FreeMonthlyAvailable`, `PaidWeeklyAvailable`, `GemCost` }.
`PledgeRequest` { `Ancient` (string) }. `MasteryRespecResult` { `Success`, `FailureCode`, `FailureReason`,
`Kind`, `GemSpent`, `NewPledge` } with `Ok(...)`/`Fail(...)` factories.
Validator: `PledgeRequestValidator` (Ancient parses to `MasteryAncient`).

### Controller — `MasteryController` `[Authorize]` (`src/ROTA.Api/Controllers/MasteryController.cs`, thin)
| Endpoint | Service | Responses |
|---|---|---|
| `GET /api/masteries` | `GetMasteriesAsync` | 200 |
| `POST /api/masteries/pledge` | `RespecAsync` | 200, 400, 404, 409, 422 |

Admin/CLI: `POST /api/admin/masteries/rating/refresh` `[AdminOnly + DB actor re-verify]` → `SnapshotRatingBoardAsync`
(mirror the stat-board refresh); CLI `mastery-refresh-rating` (`AdminCli`).

### Profile surface (Slice 2)
`PlayerProfileResponse` gains `string? ActivePledge` + `int MasteryRatingActive`; hydrated in
`PlayerService.GetProfileAsync` via an injected `IMasteryService` (exactly like `EffectiveAttack/Defense`).

---

## SLICE 1 — Mastery content + definitions + config  *(additive · LIGHT — no DB/endpoints/combat)*

- Enums: `MasteryAncient`, `MasteryActivityType`, `MasteryRespecKind`. Add `GemTransactionType.MasteryRespec = 13`,
  `LeaderboardBoard.MasteryRatingActive/Lifetime`.
- Models: `AncientDefinition`, `MasteryTierChallenge`, `MasteryChallengeItem`.
- `MasteryConfig` (POCO + defaults); bind in `Program.cs` (`Configure<MasteryConfig>(GetSection("MasteryConfig"))`)
  + add the `"MasteryConfig"` section to `appsettings.json`.
- `IMasteryDefinitionProvider` + `MasteryDefinitionProvider` (Infrastructure singleton; eager-constructed in
  `Program.cs`; loads `content/masteries.json`). **Startup validation throws** on: != 4 Ancients / duplicate
  Ancient; `globalPercentByLevel` length != 5 or any value < 0 or not non-decreasing; `tierChallenges` length != 4
  or `fromLevel` not 1..4 contiguous; empty checklist; threshold ≤ 0; unknown `activityType`; (breadth-curve
  sanity) at least one T4→5 checklist spanning ≥ 2 distinct activity types per Ancient.
- `content/masteries.json` (4 Ancients, the magnitude + checklist tables above).
- Methods: `GetAll()`, `Get(MasteryAncient)`, `GlobalPercent(ancient, level)`, `GetTierChallenge(ancient, fromLevel)`.
- Tests: provider loads 4 Ancients; magnitude lookup correct; missing Ancient throws; bad level-table length
  throws; non-monotone magnitudes throw; threshold ≤ 0 throws; unknown activityType throws.
- **Commit independently.** (Mirrors Gauntlet Slice 1: content/validation only.)

## SLICE 2 — Mastery state: entities + ownership + read API + rating  *(additive + migration · MODERATE)*

- Entities `PlayerMastery`, `PlayerMasteryActivity`, `MasteryActivityEvent` + Fluent configs (snake_case, unique
  indexes incl. partial idempotency index, FK indexes; enums-as-int with **no store default** — factory always
  sets). `Player.ActivePledgeAncient` column + `SetPledge`/`ClearPledge`. DbSets. Migration `AddMasterySystem`.
  Do NOT run `database update`.
- Repos `IPlayerMasteryRepository` (incl. `EnsureAllAsync`) + `IPlayerMasteryActivityRepository`
  (`GetForPlayerAsync` only this slice).
- `IMasteryService` + `MasteryService`: `GetMasteriesAsync` (lazy-ensure L1, build the overview DTO with
  rating/titles/per-Ancient global%+pledged%+checklist progress), `ComputeRating` (pure Formula B),
  `GetCombatModifiersAsync`/`GetLootModifiersAsync` (return L1-based modifiers; consumed in Slices 5/6),
  `SnapshotRatingBoardAsync` (Live snapshot of `MasteryRatingActive`/`Lifetime`, equal in Phase A). **No tier-up
  evaluation yet** (counters are all zero until Slice 4); progress shows X/Y with X=0.
- `MasteryController`: `GET /api/masteries`. Admin refresh endpoint + CLI `mastery-refresh-rating`. Register board
  metadata in `LeaderboardService.AllBoards`.
- `PlayerProfileResponse` + `PlayerService` hydration (`ActivePledge`, `MasteryRatingActive`); inject `IMasteryService`.
- DTOs + DI registration (`AddScoped` repos/service; `AddSingleton` provider already in S1).
- Tests: `ComputeRating` worked examples (10/14/56/4/11); lazy-ensure creates 4 L1 rows; overview hydration
  (titles at synthetic levels via direct repo seeding); profile carries pledge+rating; board snapshot upserts.
- **Commit independently. MODERATE review (rating math + lazy-create idempotency).**

## SLICE 3 — Pledge + re-spec economy  *(additive + migration · MODERATE — economy idempotency)*

- `MasteryRespecTransaction` entity + config + migration `AddMasteryRespecLedger`. `IMasteryRespecRepository`.
- `MasteryService.RespecAsync(playerId, toAncient)` — eligibility resolution **in order**:
  1. **First pledge / new-Ancient unlock free swap:** if a `respec:unlock:{p}:{toAncient}` entitlement is unconsumed
     (and for the very first pledge, always) → free swap; write ledger (Kind=NewAncientUnlock, GemCost 0).
  2. **Free monthly:** else if no `respec:free:{p}:{yyyy-MM}` row exists → free swap; write ledger (Kind=FreeMonthly).
  3. **Paid weekly:** else gate on Redis weekly cap key `respec:paid:week:{p}` (INCR; TTL→next Monday 00:00 UTC on
     first; reject `WeeklyCapReached` if already used) → `SpendGemsAsync(RespecGemCost, MasteryRespec,
     refId="respec:paid:{p}:{isoYear}-W{ww}")`; `InsufficientBalance`→`InsufficientGems`; `Charged|AlreadyProcessed`
     → write ledger (Kind=Paid, GemCost). The week-bucket gem refId is the hard double-charge backstop.
  - The swap itself is **LOSSLESS + idempotent**: `Player.SetPledge(toAncient)` + persist; mastery **levels are
    never touched**. Re-pledging the already-active Ancient → `AlreadyPledged` (no charge). Audited
    (`MasteryRespec:{Kind}`). `GrantUnlockRespecAsync` writes the unlock entitlement (called once per Ancient).
- `MasteriesController`: `POST /api/masteries/pledge` (FluentValidation → service → map FailureCode: AncientNotFound
  404, AlreadyPledged 409, WeeklyCapReached 409, InsufficientGems 422). `PledgeRequest` + validator.
  `MasteryRespecStatusDto` populated in the overview (free-monthly available? paid-weekly available? gem cost).
- Inject `IConnectionMultiplexer` (Redis weekly cap), `IGemService`, `IMasteryRespecRepository` into `MasteryService`.
- Tests: first pledge free (unlock); monthly free swap once per month; paid weekly spends gems + sets pledge;
  weekly cap reached → 409; insufficient gems → 422; **lossless** (levels unchanged after respec); re-pledge same →
  AlreadyPledged no charge; **buy-twice-charges-once** vs real gem ledger (integration); audit written.
- **Commit independently. MODERATE review (gem idempotency class + weekly-cap correctness).**

## SLICE 4 — Activity counters + tier-up leveling  *(the progression spine · DEEP)*

- `IPlayerMasteryActivityRepository.IncrementAsync` (raw-Npgsql ON CONFLICT, ambient-tx aware — clone
  `LeaderboardEntryRepository.IncrementAsync:38-73`) + `TryRecordEventAsync` (idempotency ledger insert).
- `MasteryService.RecordActivityAsync(playerId, activityType, amount, referenceId?)`: referenceId null → direct
  increment; referenceId set → `TryRecordEventAsync` first, increment only if newly recorded.
- **Wire at chokepoints** (mirror the leaderboard hook; enlist inside ambient tx where present, else best-effort
  try/catch-swallow):

  | Activity | Seam (file:line) | Tx | Style | RefId |
  |---|---|---|---|---|
  | RaidHit + RaidDamageDealt | `RaidService.cs:935` (after `RecordHit`) | enlisted | no try/catch | — |
  | RaidKill | `RaidService.cs:1021` (after `MarkDefeated`, killer = caller) | enlisted | reference-guard | `mastery:kill:{raidId}` |
  | GuildRaidContribution | `RaidService.cs:962` (after `AddContribution`) | enlisted | no try/catch | — |
  | QuestNodeCleared | `QuestService.cs:222-227`, gate `nodeJustCleared` | none | swallow | — |
  | QuestBossCleared | `QuestService.cs:232-236` | none | swallow | — |
  | GauntletRankEarned | `GauntletAdminService.cs:188` (after `ranksSettled++`) | none | swallow + idempotent | `mastery:rank:{eventId}:{playerId}` |
  | LevelGained | `StatService.cs:131` (per `newLevel`) | caller-dep | swallow + idempotent | `mastery:levelup:{playerId}:{newLevel}` |
  | EnergySpent / StaminaSpent | `EnergyService.cs:71-83` (next to leaderboard hook) | dep | swallow | — |
  | GoldEarned | `QuestService.cs:202`, `RaidService.cs:1009/1223` | mixed | swallow | — |

- **Tier-up evaluation** (off the hot path): `MasteryService` private `EvaluateTierUpsAsync(playerId)` compares each
  Ancient's `GetTierChallenge(ancient, currentLevel)` checklist against the player's counters; advances `Level`
  (monotonic, audited `MasteryLevelUp`) while the next checklist is fully met (can jump multiple tiers in one
  evaluation). Called from `GetMasteriesAsync` (read) and once at the end of `QuestService.AttemptQuestAsync`
  (best-effort). The combat/loot modifier reads in Slices 5/6 then reflect real evolving levels.
- Inject `IMasteryService` (or a focused `IMasteryProgressService`) at each service; keep the combat-path call to a
  single `RecordActivityAsync` next to the existing leaderboard call.
- Tests (mocked repos / seeded counters): each seam increments the right counter; raid-hit replay (Redis cached) never
  double-counts (structurally — call sits inside the mutate body); gauntlet re-settle idempotent; multi-level-per-hit
  loop records once per level; tier-up advances when checklist met and not before; breadth-curve T4→5 requires all
  cross-system items; level never decreases; existing RaidService/QuestService/Gauntlet tests still pass.
- **Commit independently. DEEP review (chokepoint correctness, idempotency, no hot-path tier evaluation, no double-count).**

## SLICE 5 — Combat integration: Wrath + Bulwark  *(DEEP / combat money path)*

- Inject `IMasteryService` (scoped) into `RaidService` (add field + ctor param **before** the trailing
  `Random? random = null`, assign in the ctor body — the `_guildEconomy` dep at `RaidService.cs:101/182` is the
  precedent).
- Load `var masteryMods = await _mastery.GetCombatModifiersAsync(playerId, ct);` once per hit, near the
  `combat`/legion loads (≤4-row indexed read; legion-less + mastery-less players unaffected).
- **Wrath:** inside the `if (activeLegion is not null)` block, add the Wrath **percent** into `totalLegionBonus`
  **before** `RaidService.cs:701` (`double bonusFraction = totalLegionBonus / 100.0;`). It then flows once through
  `rawLegionPower = unitSum × (1 + bonusFraction)` — additive with `PowerBonus` + Σ`General.LegionBonus`, applied
  before the trophy `*=` stage (`:717`) and PowerScaling (`:720`). **Do not** add a second multiplier (no
  double-touch). Wrath = pledged? `global × pledgeMultiplier` : `global`. No active legion → Wrath no-ops (intended).
- **Bulwark:** at `RaidService.cs:909-911`, compute
  `double flatDamagePct = combat.FlatDamagePercent + (lockedRaid.GuildId is not null ? masteryMods.BulwarkGuildDamageFraction : 0.0);`
  and apply `damageFinal = Math.Max(1, (long)(damageFinal × (1 + flatDamagePct)));`. **Widen the `if (combat.FlatDamagePercent > 0)`
  guard** to `if (flatDamagePct > 0)` so Bulwark fires even when gear FlatDamagePercent is 0. Gate strictly on
  `lockedRaid.GuildId` (the reloaded entity, like the contribution/aura forks). Bulwark fraction is clamped to
  `BulwarkMaxGuildDamagePercent/100.0` inside `GetCombatModifiersAsync`. Pledged doubles, then clamps.
- `RaidHitResponse` additive display fields: `long WrathLegionBonus` (0 when no legion/Wrath), `long BulwarkBonus`
  (0 on non-guild raids). Surface in the result mapping.
- Tests (seeded RNG, mocked `IMasteryService`): mastery-less hit byte-for-byte identical to before (regression);
  Wrath adds the expected legion power for a known loadout+level; Wrath pledged ≈ 2× global; Wrath ignored when no
  active legion; Bulwark applies only when `GuildId != null`; Bulwark NOT applied on a normal/gauntlet raid; Bulwark
  clamped at the hard cap; Bulwark stacks additively with gear FlatDamagePercent; existing mount/magic/crit/trophy
  assertions unchanged.
- **Commit independently. DEEP review (formula order, single-touch Wrath, GuildId gate, guard widening, no double-count).**

## SLICE 6 — Loot integration: Hoard + Discernment sigil-find  *(MODERATE)*

- `MasteryService.GetLootModifiersAsync` returns `HoardDropMult` (= `1 + hoardFraction + breadthMicroBonus`),
  `HoardGoldMult` (same), `DiscernmentSigilFindMult` (= `1 + discFraction`). (DiscernmentQualityMult wired in Slice 7.)
- **Quest path** (`QuestService`): fetch the modifiers alongside the existing Discernment read (`QuestService.cs:265-267`);
  pass `hoardDropMult` into `ProcessQuestLootAsync` and multiply inside the `Scale` closure
  (`QuestService.cs:363-368`: `boosted = baseChance × (1 + Disc×k) × hoardDropMult`), one edit covering
  item/magic/unit/legion/gear chance drops; multiply quest gold at `QuestService.cs:192`
  (`goldReward = (int)(quest.GoldReward × rewardMult × hoardGoldMult)`); scale the post-first-clear sigil chance at
  `QuestService.cs:284` (`_random.NextDouble() < Math.Min(1.0, quest.SigilDropChance × sigilFindMult)`) — leave the
  guaranteed-first-clear branch (`:277-281`) untouched (`SigilFindAppliesToFirstClear` locked false).
- **Raid path** (`RaidService`): apply `hoardGoldMult` to on-hit gold (`:977`, before `AddGold` at `:1009`) and to
  kill-reward gold (`:1220`) **and the DTO recompute (`:1343`)** so they stay in sync. Raid threshold drops
  (`:1273,1283,1290,1297` + gear `:1302`) currently roll raw `drop.Chance` with no scaling — introduce a local
  `Scale`-style helper that applies `hoardDropMult` (and is ready for the Discernment quality roll in Slice 7).
  Per-participant modifiers fetched inside the `foreach` participant loop (`:1208`, after the null-check `:1216`).
- Quest reward path is non-atomic by design (`QuestService.cs:11` PHASE-2 note) — the new multipliers inherit that;
  they do not make it worse. Raid path is atomic inside the advisory-lock tx.
- Tests: Hoard scales quest chance-drop rate; Hoard scales quest + raid gold (grant and DTO match); Discernment
  scales the post-first-clear sigil chance but never the guaranteed first; mastery-less → unchanged rates/gold;
  pledged Hoard ≈ 2× global; micro-bonus respects its cap.
- **Commit independently. MODERATE review (multiplier placement, gold DTO sync, sigil first-clear untouched).**

## SLICE 7 — Discernment drop-quality (rarity-upgrade)  *(MODERATE · CUTTABLE — flag for owner)*

- Additive optional `string? UpgradesTo` on `ItemDefinition` + `GearDefinition` (content models only — **no
  migration**). Provider startup validation: if set, `UpgradesTo` resolves in the same provider, is strictly higher
  `ItemRarity`, and ≤ `Orange`.
- `MasteryService.GetLootModifiersAsync` adds `DiscernmentQualityChance` (a fraction, pledged-doubled, capped).
- Rarity-upgrade roll at **drop-resolution** (after a chance drop fires): quest items `QuestService.cs:380-381`,
  quest gear `:419-420`; raid threshold drops `RaidService.cs:1271-1303`. On a successful Discernment-scaled roll,
  if the dropped def has `UpgradesTo`, substitute it (resolve once; clamp at Orange). Items without `UpgradesTo`
  never upgrade (safe default). Guaranteed drops are never upgraded.
- Seed a handful of `upgradesTo` links in existing item/gear content as the starter quality ladder (e.g. White→Green
  within a family) so the mechanic is demonstrable; full content ladder is a follow-up.
- Tests: upgrade fires on a successful roll and substitutes the higher def; clamps at Orange; def without `upgradesTo`
  never upgrades; no Discernment → no upgrades; guaranteed drops never upgrade.
- **Commit independently. MODERATE review.** *(If the owner defers this slice, Phase A still ships a complete
  Discernment via sigil-find from Slice 6; only the rarity-upgrade half waits.)*

---

## Constraints (every slice)

- Domain entities: private setters, no EF attributes; state via methods/factories; monotonic `PlayerMastery.Level`.
- EF Fluent only, snake_case; every table `id`/`created_at`/`updated_at`/`is_deleted` (append-only ledgers: `id` +
  `created_at` only); FKs indexed; unique partial idempotency indexes `WHERE reference_id IS NOT NULL`; enum columns
  int with no store default (factory sets) — `HasSentinel` only ever for a non-zero store default.
- Content providers singletons, eager-constructed in `Program.cs`, throw at startup on invalid data.
- Services: interface in `Application/Interfaces`, impl in `Application/Services`; controllers thin; `PlayerId` from
  JWT `sub`; server-authoritative; FluentValidation before the service; **every state change → audit_log** via the
  `Audit(...)` helper pattern.
- Reuse, don't fork: Wrath via `legionBonusFraction`; Bulwark via `FlatDamagePercent` gated on `GuildId`;
  Hoard/Discernment via the quest-loot `Scale` + gold sites; activity counters via the leaderboard-hook pattern;
  gem spend via `IGemService`; weekly cap via the Redis INCR+TTL idiom. No new combat path.
- Do NOT run `dotnet ef database update`. Build 0 warnings; all tests green before committing a slice. Update
  `PROJECT_STATE.md` count + `docs/ROTA_Function_Reference.md` as you go. No co-author trailer. One branch + one
  merge per slice; never bundle.

---

## Confirmed decisions (owner, 2026-06-07)

1. **Discernment "drop quality" (Slice 7) — BUILD IT.** Opt-in `upgradesTo` content pointer + Discernment-scaled
   upgrade roll (avoids a global item-family system), clamped to Orange, with a seeded starter quality ladder.
2. **Weekly cap mechanism — Redis INCR+TTL** (per the design's "weekly cap in Redis"), with the gem-ledger
   week-bucket refId (`respec:paid:{p}:{isoYear}-Www`) as the double-charge backstop.
3. **TUNE magnitudes — confirmed as the starting point** (Wrath 2.5/~5, Bulwark 0.5/~1 cap, Hoard & Discernment
   4/~8; pledge ×2; per-level ramps + challenge thresholds per the tables above). Re-tunable via `content/masteries.json`
   + `MasteryConfig` without code changes.
4. **Capped breadth micro-bonus — LEFT OFF.** Mechanism built and hard-capped at 2.0%, but
   `MasteryConfig.BreadthMicroBonusPercent = 0.0` (off) until the owner sizes it.
5. **"Free swap on new-Ancient unlock" — confirmed:** Phase A models it as **first-pledge-free** plus a
   `GrantUnlockRespecAsync` hook that Phase B's Rise calls when it awakens a *new* Ancient.

## Build environment note (2026-06-07)
`main` already contains the merged guild S3a/S3b + Gauntlet code (verified: `RaidService` `GuildId` fork +
`GuildEconomyRepository.TrySpendPoolAsync` present on `main`), so every slice branches cleanly off `main` and all
combat/loot hooks resolve. (The CLAUDE.md "S3a+S3b on branches" line is stale.)
```
