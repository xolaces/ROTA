# System 14 — Raid Magic (spec + sliced task queue)

*Spec locked 2026-05-31. Research-backed against the DotD wiki. Build against this exactly.*
*This is a LARGE epic broken into independent slices. Build ONE slice per branch, build+test green,
commit/merge independently, then move to the next. Do NOT bundle slices. Auditor reviews after a batch.*

---

## Core insight (read first)

A raid magic is a **shared, raid-scoped proc**: *"any raid member's attack has X% chance to deal Y%
bonus damage."* This is mechanically identical to the **mount proc** and the `ProcChanceFlat`/
`ProcAmountFlat` already shipped in v0.2.5's `ConditionalBonusEvaluator`. The only difference is scope —
a mount proc reads the player's gear; a magic proc reads the **raid's applied-magic list**.

**Reuse the existing proc pipeline and evaluator.** This is NOT a new combat system. Slice 4 folds magic
procs into the same proc phase the mount proc already uses.

---

## Locked design decisions (owner-confirmed)

1. **Magic = a shared raid-scoped proc.** Effect fires on *any* participant's hit.
2. **Ownership model = permanent owned unlock.** A player owns a magic (acquired via the economy);
   applying it to a raid is free and repeatable. The economy sink is *acquisition*, not application.
3. **Slots scale with raid size, plateau at 5** (config-driven, adjustable):

   | RaidSize | Magic slots |
   |----------|-------------|
   | Personal | 1 |
   | Small    | 2 |
   | Medium   | 3 |
   | Large    | 4 |
   | Titanic  | 5 |

4. **World raids are dev-only for magic.** On a raid whose definition `Tier` ∈ `MagicConfig.DevOnlyTiers`
   (default `["World"]`), only an **Admin** may apply or remove magic. This is the anti-griefing rule —
   serious shared content has curated magic; players use magic freely on all other tiers.
5. **One magic per player per raid** (non-world). A participant may occupy at most one slot. Admins on
   world raids are exempt (they curate, up to the slot cap).
6. **Removal:** non-world → summoner only; world → Admin only. (Guild-officer removal deferred.)
7. **No duplicate magic per raid** — the same `MagicDefinition` cannot occupy two slots on one raid.
8. **Aggregate cap** — total magic proc bonus per hit is bounded by `MagicConfig.MaxAggregateProcBonus`.
9. **Deferred (do NOT build — documented at the bottom):** synergy combos ("extra if X cast"),
   raid-type targeting tags, Gauntlet magics + trophies, negative/modifier magics, healing/defense,
   consumable-magic model, guild-officer removal.

---

## Data model (whole epic)

### Content: `MagicDefinition` (`content/magics.json`)
```
id            string   e.g. "magic_smite"
name          string
description   string
rarity        ItemRarity     (Grey..Orange — reuse existing enum)
category      MagicCategory  (Damage/Crit/Gold/Leveling/Utility — informational/UI only)
effectType    MagicEffectType (DamageProc/CritChanceFlat/GoldProc/XpProc)
procChance    double   0..1   (ignored for CritChanceFlat which is always-on)
procAmount    double          (DamageProc/GoldProc: fraction of base, e.g. 0.6 = +60%;
                               XpProc: multiplier, e.g. 2.0 = double; CritChanceFlat: +chance, e.g. 0.05)
conditions    ConditionalBonus[]   (REUSE v0.2.5 shape for ownership scaling; empty for now)
stacks        bool     (false = effect does not stack with other magics of same effectType)
iconPath      string
acquisition   string   (informational: where it drops/crafts)
```

### Enums (`src/ROTA.Domain/Enums/`)
- `MagicCategory { Damage, Crit, Gold, Leveling, Utility }`
- `MagicEffectType { DamageProc, CritChanceFlat, GoldProc, XpProc }`

### Entities (`src/ROTA.Domain/Entities/`) — private setters, no EF attributes
- `PlayerMagic` — `Id, PlayerId, MagicDefinitionId, Quantity(=1), CreatedAt, UpdatedAt, IsDeleted`.
  Unique `(player_id, magic_definition_id)`. (Quantity reserved for a future consumable model.)
- `RaidMagic` — `Id, ActiveRaidId, MagicDefinitionId, AppliedByPlayerId, CreatedAt, UpdatedAt, IsDeleted`.
  Unique `(active_raid_id, magic_definition_id)` (no duplicate magic). Index `active_raid_id`.

### Config: `MagicConfig` (appsettings, bound via `IOptions`)
```
SlotsBySize           { Personal:1, Small:2, Medium:3, Large:4, Titanic:5 }
DevOnlyTiers          [ "World" ]
MaxAggregateProcBonus 5.0     (cap total magic bonus at 5× base damage per hit — tune later)
```

---

## Damage formula (final order — implemented across slices 4 & 5)

```
base       = (effectiveATK×4 + effectiveDEF) × hitSize × RNG[0.85,1.15]
+ mount proc          (existing: chance → + procPercent × base)
+ Σ magic DamageProcs (slice 4: each chance → + procAmount × base; total magic bonus capped at
                       MaxAggregateProcBonus × base)
× crit                (existing: × critMultiplier; crit chance += Σ magic CritChanceFlat — slice 5)
× (1 + FlatDamagePercent)   (existing v0.2.5 conditional)
→ damageFinal = max(1, …)
```
Magic proc damage is added to the **attacker's** hit, so it counts toward that player's contribution
(rewards) naturally. Gold/Xp magic procs (slice 5) modify the existing on-hit gold/XP grant.

---

## SLICE 1 — Magic content + definitions  *(additive · LIGHT review)*

**Goal:** the content layer. No DB, no combat.

Create:
- `src/ROTA.Domain/Enums/MagicCategory.cs`, `MagicEffectType.cs`
- `src/ROTA.Application/Models/MagicDefinition.cs`
- `src/ROTA.Application/Interfaces/IMagicDefinitionProvider.cs` (`GetById`, `GetAll`)
- `src/ROTA.Infrastructure/Services/MagicDefinitionProvider.cs` (singleton; pattern = `ItemDefinitionProvider`;
  startup validation: throw on duplicate id, `procChance` outside 0..1, negative `procAmount`)
- `src/ROTA.Api/content/magics.json` — starter set below
- Register `IMagicDefinitionProvider` singleton in `ServiceCollectionExtensions`

Starter content (`magics.json`):

| id | name | rarity | category | effectType | procChance | procAmount |
|----|------|--------|----------|-----------|-----------|-----------|
| magic_whetstone | Whetstone | White | Damage | DamageProc | 1.00 | 0.03 |
| magic_lesser_poison | Lesser Poison | White | Damage | DamageProc | 0.04 | 0.02 |
| magic_poison | Poison | Green | Damage | DamageProc | 0.07 | 0.04 |
| magic_greater_poison | Greater Poison | Blue | Damage | DamageProc | 0.15 | 0.08 |
| magic_smite | Smite | Blue | Damage | DamageProc | 0.10 | 0.60 |
| magic_blessing_of_might | Blessing of Might | Blue | Damage | DamageProc | 0.08 | 0.80 |
| magic_impending_doom | Impending Doom | Purple | Damage | DamageProc | 0.01 | 3.11 |
| magic_expose_weakness | Expose Weakness | Green | Crit | CritChanceFlat | 0.00 | 0.05 |
| magic_midas_touch | Midas Touch | Green | Gold | GoldProc | 0.05 | 1.00 |
| magic_kindling | Kindling | White | Leveling | XpProc | 0.04 | 2.00 |

Tests: provider loads all 10; duplicate id throws; `procChance` 1.5 throws.
**Commit independently.**

---

## SLICE 2 — Magic ownership  *(additive + migration · LIGHT review)*

**Goal:** players own magics; list them.

- `PlayerMagic` entity + `PlayerMagicConfiguration` (Fluent, snake_case, unique index)
- `IPlayerMagicRepository` (`GetOwnedAsync`, `FindAsync`, `UpsertAsync`) + impl
- `DbSet<PlayerMagic>` in `RotaDbContext`; migration `AddPlayerMagics` (do NOT run `database update`)
- `IMagicService` + `MagicService`: `GetOwnedMagicsAsync` (hydrate with definition)
- `MagicController` `[Authorize]`: `GET /api/magics` → owned magics
- DTO `OwnedMagicResponse`
- Register repo + service
- Tests: list owned (hydrated), zero-owned returns empty
**Commit independently.**

---

## SLICE 3 — Application + slots + world gate  *(concurrency + authz + migration · DEEP review)*

**Goal:** apply/remove magic to a raid, enforce slots and the dev-gate. Magics are **inert** (no combat
effect yet — that's slice 4).

- `RaidMagic` entity + config (Fluent, snake_case, unique `(active_raid_id, magic_definition_id)`, FK index)
- `IRaidMagicRepository` (`GetForRaidAsync` non-deleted, `CountForRaidAsync`, `FindAsync`, `CreateAsync`,
  `SoftDeleteAsync`) + impl; `DbSet<RaidMagic>`; migration `AddRaidMagics`
- `MagicConfig` bound from appsettings; register `IOptions<MagicConfig>`
- `IMagicService.ApplyMagicAsync(playerId, raidId, magicDefinitionId, isAdmin, ct)` →
  validation order: raid exists & active; **world gate** (def.Tier ∈ DevOnlyTiers ⇒ require `isAdmin`);
  player owns the magic; (non-world) player is summoner or participant; (non-world) player has no existing
  magic on this raid (one-per-player); not a duplicate magic; slot available
  (`CountForRaidAsync < SlotsBySize[size]`). **Acquire the raid advisory lock for the count→insert** to
  prevent slot over-allocation (reuse the `ActiveRaidRepository` advisory-lock pattern). Audit on success.
- `IMagicService.RemoveMagicAsync(playerId, raidId, magicDefinitionId, isAdmin, ct)` →
  world ⇒ require `isAdmin`; else require caller == raid summoner. Soft-delete. Audit.
- `MagicController`: `POST /api/raids/{raidId}/magics` body `{ magicDefinitionId }`;
  `DELETE /api/raids/{raidId}/magics/{magicDefinitionId}`. Pass `User.IsInRole`/Admin policy result as `isAdmin`.
- Failure → 400/403/404/409/422 with codes (`MagicApplyFailureCode`).
- Tests: slot cap enforced; world gate (Admin allowed, non-admin denied); duplicate magic rejected;
  non-owner denied; non-participant denied; one-per-player enforced; summoner-only removal; world removal
  Admin-only. **Add an integration test** for the slot-cap race (advisory lock holds).
**Commit independently. DEEP review (concurrency + authorization + migration).**

---

## SLICE 4 — Damage proc effects in combat  *(combat-critical · DEEP review)*

**Goal:** applied `DamageProc` magics actually deal bonus damage. Reuse the proc pipeline.

- In `RaidService.HitRaidAsync`, inside the advisory-lock callback, **after the mount proc block**:
  load `await _raidMagics.GetForRaidAsync(raidId, ct)` (≤5 rows, cheap, in the same tx).
  For each magic with `effectType == DamageProc`: roll `procChance`; on success add
  `procAmount × baseValue` to a `magicBonus` accumulator (`baseValue` = the pre-proc base hit, same
  value the mount proc uses). Apply ownership-scaling `conditions` via `ConditionalBonusEvaluator` if the
  magic declares any. Cap total `magicBonus ≤ MaxAggregateProcBonus × baseValue`. Add to `damageFinal`
  before crit (consistent with the mount proc).
- Inject `IRaidMagicProvider`/repo into `RaidService` (scoped).
- `RaidHitResponse`: add `long MagicProcBonus` and `List<MagicProcDTO> MagicProcs` ({ name, bonus }) for the
  future raid log.
- Confirm magic damage flows into `participant.RecordHit(damageFinal)` (contribution) — it does, since it's
  in `damageFinal`.
- Tests (seeded RNG, mock `IRaidMagicRepository`): single magic fires/doesn't; multiple magics stack;
  aggregate cap clamps; magic bonus included in `DamageDealt` and participant total.
**Commit independently. DEEP review (formula order, cap, reward interaction — the money/combat path).**

---

## SLICE 5 — Utility magic effects (crit / gold / XP)  *(moderate review)*

**Goal:** wire the non-damage effect types into the existing hit grants.

- `CritChanceFlat`: before the crit roll in `HitRaidAsync`, add `Σ procAmount` of applied `CritChanceFlat`
  magics to the crit chance (clamp with the existing crit cap). Reuses the discernment-crit path.
- `GoldProc`: after the base gold grant, for each applied `GoldProc` magic roll `procChance`; on success add
  `procAmount × goldGained`. (Respect `stacks=false` if set.)
- `XpProc`: for each applied `XpProc` magic roll `procChance`; on success multiply `xpGained` by
  `procAmount` (e.g. ×2). Apply once per magic; document stacking.
- `RaidHitResponse`: surface any crit-chance/gold/xp magic contribution if useful.
- Tests: crit-chance magic raises crit odds; gold magic adds gold on proc; xp magic multiplies xp on proc.
**Commit independently. Moderate review (reuses existing crit/gold/xp paths).**

---

## SLICE 6 — Magic economy (acquisition)  *(moderate review)*

**Goal:** players can EARN magics (the sink). Application stays free (Model A).

- `IMagicService.GrantMagicAsync(playerId, magicDefinitionId, ct)` — idempotent upsert into `PlayerMagic`.
- **Loot drops:** extend loot tables to grant magics (a `magicDrops` list per difficulty tier, mirroring
  `itemDrops`), wired into `RaidService`/`QuestService` reward distribution.
- **Gem shop:** `POST /api/magics/buy` body `{ magicDefinitionId }` — spend gems (price from a
  `cost`/`gemPrice` field on `MagicDefinition`; add it in this slice), grant on success, reject on
  insufficient balance. Idempotency not required (buying a duplicate just no-ops the upsert or is allowed —
  decide and document).
- Tests: grant from loot; buy with gems (success + insufficient-balance reject); grant idempotency.
**Commit independently. Moderate review (economy + idempotency).**

---

## Deferred — document only, do NOT build

- **Synergy combos** ("Extra X% damage if [other magic] is cast/owned") — the combinatorial web that made
  DotD's 191 magics unmaintainable. Add later, deliberately.
- **Raid-type targeting** ("Extra vs Dragon/Demon raids") — needs raid-type tags on definitions.
- **Gauntlet magics + trophies** (SMITE, Blessing of Mathala) — tie to the future Gauntlet system; special
  lifecycle (removed on Gauntlet summon), trophy scaling, loot boosts.
- **Negative / modifier magics** (Elite/Deadly reduce slots or damage) — `SlotsBySize` already supports a
  modifier hook; wire when raid modifiers land.
- **Healing / defensive magics** — once player health/boss-retaliation matters.
- **Consumable-magic model** — `PlayerMagic.Quantity` is reserved for it.
- **Guild-officer removal** — when the guild system lands.

---

## Constraints (every slice)

- Domain entities: private setters, no EF attributes; state via methods/factories only.
- EF: Fluent API only, snake_case for all tables/columns/indexes; every table has `id`/`created_at`/
  `updated_at`/`is_deleted`; FKs indexed.
- Content providers: singletons; throw at startup on invalid data.
- Controllers thin; `PlayerId` from JWT `sub` only; server-authoritative.
- Concurrency-critical slot allocation uses the raid advisory-lock pattern.
- Do NOT run `dotnet ef database update` (owner applies migrations).
- Build 0 warnings; all tests green before committing a slice. Update `PROJECT_STATE.md` test count and the
  Function Reference as you go.
- No co-author trailer on commits. One branch + one merge per slice; never bundle slices.
