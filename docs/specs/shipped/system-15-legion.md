# System 15 — Legion (spec + sliced task queue)

*Spec locked 2026-05-31. Grounded in the DotD wiki (Legions / Stats / Proc pages, saved in
docs/research/dotd-wiki). Build against this exactly. LARGE epic — one slice per branch, build+test
green, commit/merge/tag/push independently, never bundle. Auditor reviews after a batch.*

---

## Core insight

Unit, legion, and commander abilities are **procs** — DotD literally defines Proc as "chance to trigger
a bonus ability." That is the *same* `ConditionalBonusEvaluator` + proc phase already shipped for gear
(mount) and magic (System 14). Legion combat is an **extension of the existing `HitRaidAsync` pipeline**,
not a new combat system. Reuse it.

## Locked design decisions (owner-confirmed)

1. **Legion power is a SEPARATE additive damage term** in the raid hit, using DotD's per-unit-type
   coefficients. Crit multiplies the combined (character + legion) total. *(Decision 1B)*
2. **Legion is the PRIMARY damage contributor but tuned below Dawn's ~90%** via `LegionConfig.PowerScaling`.
   **Mount procs stay significant** because they scale off the combined base (character + legion), as in
   Dawn. Target feel: legion ≈ 50–70% of a hit (tune by playtest), not 90%. *(Decision 2a, tuned)*
3. **Launch with Generals + Troops only.** Unit-type→coefficient is config, so **Armaments**
   (Relic/Support/Siege) drop in later as pure content, no code change. *(Decision 3a, b-in-mind)*
4. **Slot type engine (Race/Role/Attribute) built now**; starter legions are mostly-open with a few
   specialist legions whose type-locked slots carry higher Power Bonus. *(Decision 4a)*

**Deferred (document only, do NOT build):** Armaments (Relic/Support/Siege), unit leveling, Gauntlet
trophies/gorgets that boost legion power, vs-raid-type bonuses, multi-copy troop stacking, Auto-Assign,
Commander beyond slice 5.

---

## Damage formula (locked — implemented in slice 4)

```
multiplier  = 0.85 + RNG×0.30                                  [existing single RNG roll]
charBase    = (effATK×4 + effDEF) × hitSize × multiplier        [existing]
legionPower = LegionPower(activeLegion) × hitSize × multiplier  [NEW — same RNG + hitSize as charBase]
preProc     = charBase + legionPower                           [combined base]
 + mount proc           (preProc × ProcPercent)                [scales off combined → mounts stay big]
 + Σ magic DamageProcs  (off preProc, aggregate-capped)        [System 14, unchanged]
 + Σ unit-ability procs (off preProc, capped — see slice 4)    [NEW, reuses evaluator]
 × crit                 (multiplies the combined total)        [existing]
 × (1 + FlatDamagePercent)                                     [existing]
 → damageFinal = max(1, …)

LegionPower(legion) = unitSum × (1.0 + legionBonusFraction) × LegionConfig.PowerScaling
  unitSum             = Σ over FILLED slots of  coeff[unitType].Atk × unit.BaseAttack
                                              + coeff[unitType].Def × unit.BaseDefense
  coeff (config):  General {2.0, 0.4}   Troop {1.44, 0.36}
                   [future Relic {4.0,0.8}  Support {3.0,0.6}  Siege {2.0,0.4}]
  legionBonusFraction = (legionDef.PowerBonus + Σ general.LegionBonus
                         [+ future: Gauntlet trophies/gorgets, vs-raid-type]) / 100.0
```
`PowerScaling` is the master dial for decision 2 (lower → legion less dominant). Legion power flows into
`preProc`, so it counts toward contribution, gets crit-multiplied, and mount/magic procs scale off it.

---

## Data model (whole epic)

### Enums (`src/ROTA.Domain/Enums/`)
- `UnitType { General, Troop }`  *(Relic/Support/Siege added later via config — leave the enum open)*
- `UnitRace { Human, Undead, Oroc, Dwarf, Elf, Beast, Construct, Demon, Any }`  *(ROTA's set; tune freely)*
- `UnitRole { Tank, Melee, Ranged, Healer, Special, Any }`
- `UnitAttribute { Strength, Agility, Intellect, Wisdom, Special, Any }`
- `SlotConstraintType { None, Race, Role, Attribute }`
- `LegionSlotFamily { General, Troop }`

### Content models (`src/ROTA.Application/Models/`, JSON in `content/`)
**`UnitDefinition`** (`content/units.json`)
```
id, name, description, unitType (UnitType), rarity (ItemRarity),
baseAttack (int), baseDefense (int),
race (UnitRace), role (UnitRole), attribute (UnitAttribute),
ability (UnitAbility?  — null if none),  isPassive (bool),
legionBonus (double — % added to legion bonus; generals only, 0 otherwise),
iconPath, acquisition
```
**`UnitAbility`** = a proc, REUSING the System 14 shape: `{ procChance (0..1), procAmount (double, fraction
of preProc), conditions (ConditionalBonus[]) }`. Fires in the hit proc phase like a magic DamageProc.

**`LegionDefinition`** (`content/legions.json`)
```
id, name, description, rarity, powerBonus (double — %),
generalSlots (SlotSpec[]),  troopSlots (SlotSpec[]),
iconPath, acquisition
```
**`SlotSpec`** = `{ constraintType (SlotConstraintType), constraintValue (string — e.g. "Tank"/"Strength"/null) }`

### Entities (`src/ROTA.Domain/Entities/`) — private setters, no EF attributes, snake_case
- `PlayerUnit`  — `Id, PlayerId, UnitDefinitionId, Quantity(=1), created/updated/IsDeleted`.
  Unique `(player_id, unit_definition_id)`.
- `PlayerLegion` — `Id, PlayerId, LegionDefinitionId, IsActive(bool), created/updated/IsDeleted`.
  Unique `(player_id, legion_definition_id)`. Exactly one `IsActive=true` per player (service-enforced).
- `PlayerLegionSlot` — `Id, PlayerId, LegionDefinitionId, SlotFamily (LegionSlotFamily), SlotIndex (int),
  UnitDefinitionId, created/updated/IsDeleted`. Unique `(player_id, legion_definition_id, slot_family,
  slot_index)`. The saved loadout per legion.

### Config: `LegionConfig` (appsettings, IOptions; safe C# defaults)
```
PowerScaling        double  default 1.0   (master dominance dial — tune down by playtest)
UnitCoefficients    { General:{Atk:2.0,Def:0.4}, Troop:{Atk:1.44,Def:0.36} }
MaxUnitProcBonus    double  default 5.0   (aggregate cap on unit-ability procs × preProc)
```

---

## SLICE 1 — Unit + Legion content/definitions  *(additive · LIGHT)*

- Enums above; `UnitDefinition`/`UnitAbility`/`LegionDefinition`/`SlotSpec` models.
- `IUnitDefinitionProvider` + `ILegionDefinitionProvider` (singletons; pattern = `MagicDefinitionProvider`;
  startup validation: unique ids; `procChance` 0..1; slot constraintValue parses to the right enum;
  generalSlots only reference UnitType.General etc.).
- `content/units.json` + `content/legions.json` — starter roster below.
- Register providers.
- Tests: providers load; duplicate id throws; bad constraintValue throws; bad procChance throws.
**Commit independently.**

**Starter units** (`units.json` — tune numbers freely; generals stronger + carry abilities/legionBonus):

| id | name | type | rarity | ATK | DEF | race | role | attr | ability (chance→amount) | legionBonus |
|----|------|------|--------|-----|-----|------|------|------|------------------------|-------------|
| gen_ironward | Ironward the Steadfast | General | Green | 80 | 60 | Human | Tank | Strength | 8%→+40% | 5 |
| gen_ashblade | Ashblade | General | Blue | 140 | 50 | Human | Melee | Agility | 10%→+60% | 10 |
| gen_sylvaire | Sylvaire | General | Blue | 120 | 70 | Elf | Ranged | Intellect | 6%→+90% | 10 |
| gen_morvath | Morvath the Unliving | General | Purple | 200 | 90 | Undead | Special | Wisdom | 5%→+120% | 20 |
| troop_militia | Conscript Militia | Troop | White | 30 | 20 | Human | Melee | Strength | — | 0 |
| troop_archers | Wood Archers | Troop | Green | 45 | 25 | Elf | Ranged | Agility | 6%→+15% | 0 |
| troop_pikemen | Iron Pikemen | Troop | Green | 40 | 40 | Human | Tank | Strength | — | 0 |
| troop_acolytes | Shadow Acolytes | Troop | Blue | 60 | 35 | Undead | Special | Wisdom | 5%→+30% | 0 |

**Starter legions** (`legions.json`):

| id | name | rarity | PB% | general slots | troop slots |
|----|------|--------|-----|---------------|-------------|
| legion_warband | Free Warband | White | 50 | 2× None | 2× None |
| legion_vanguard | Dawn Vanguard | Blue | 120 | 3× None | 3× None |
| legion_ironlegion | The Iron Legion | Purple | 250 | 1×Tank, 1×Melee, 1×Ranged | 2× Strength |

---

## SLICE 2 — Ownership  *(additive + migration · LIGHT)*

- `PlayerUnit` + `PlayerLegion` entities + Fluent configs (snake_case, unique indexes); migration
  `AddLegionOwnership`. `DbSet`s. Do NOT run `database update`.
- `IPlayerUnitRepository` / `IPlayerLegionRepository` (Get owned, Find, Upsert; legion: GetActive).
- `ILegionService` + `LegionService`: `GetOwnedUnitsAsync`, `GetOwnedLegionsAsync` (hydrate w/ defs).
- `LegionController` `[Authorize]`: `GET /api/units`, `GET /api/legions`.
- DTOs: `OwnedUnitResponse`, `OwnedLegionResponse`.
- Tests: list owned units/legions (hydrated), zero-owned empty.
**Commit independently.**

## SLICE 3 — Legion assembly + power computation  *(validation-heavy · MODERATE)*

Single-player loadout state — no shared-resource race, so **no advisory lock needed** (unlike magic slots).

- `PlayerLegionSlot` entity + config + migration `AddLegionSlots`.
- `IPlayerLegionSlotRepository` (GetForLegion, Find, Upsert, SoftDelete).
- `LegionService`:
  - `SetActiveLegionAsync(playerId, legionDefId)` — owns it; set IsActive, clear IsActive on others.
  - `AssignSlotAsync(playerId, legionDefId, family, index, unitDefId)` — validate: legion owned; slot
    exists in def (family + index < slot count); unit owned (qty>0); `unit.unitType` matches family;
    unit satisfies the slot's `SlotSpec` constraint (None/Race/Role/Attribute); unit not already in
    another slot of THIS legion. Upsert the slot.
  - `ClearSlotAsync(...)` — soft-delete the slot row.
  - `ComputeLegionPower(playerId, legionDefId)` — the LegionPower formula above (no PowerScaling here;
    that's combat-only — return the raw power + the bonus breakdown for display).
  - `GetLegionDetailAsync` — slot layout + assignments + computed power.
- `LegionController`: `PUT /api/legions/{id}/active`, `PUT /api/legions/{id}/slots/{family}/{index}`
  (body `{ unitDefinitionId }`), `DELETE /api/legions/{id}/slots/{family}/{index}`,
  `GET /api/legions/{id}`. Failure codes → 400/404/409/422.
- Tests: assign success; not-owned unit; wrong unit-type for family; constraint mismatch (race/role/attr);
  slot-index out of range; duplicate unit in same legion; set-active flips others; compute power matches
  the formula for a known loadout.
**Commit independently. MODERATE review (validation correctness + the power math).**

## SLICE 4 — Combat integration  *(DEEP / combat-money path)*

- Inject `IPlayerLegionRepository` + `IPlayerLegionSlotRepository` + `IUnitDefinitionProvider` +
  `IOptions<LegionConfig>` into `RaidService` (scoped).
- In `HitRaidAsync`, inside the advisory-lock callback, after `charBase`/`preProc` are computed:
  - Load the player's **active** legion + its slots (≤ ~12 rows). Compute `legionPower` per the formula
    (× hitSize × the same `multiplier`), apply `PowerScaling`, add to `preProc` **before** the mount-proc
    block so mount/magic procs scale off the combined base.
  - **Unit-ability procs:** for each filled slot whose unit has an `ability`, roll `procChance`; on hit add
    `procAmount × preProc`; evaluate `conditions` via `ConditionalBonusEvaluator` when declared; accumulate
    and cap at `MaxUnitProcBonus × preProc` (separate cap from magic). Add to `damageFinal` in the proc
    phase. Passive abilities (`isPassive`) that are stat/legion-bonus types are already folded into
    `legionPower`; only DamageProc-style abilities roll here.
- `RaidHitResponse`: add `long LegionPower` and `List<MagicProcDTO> UnitProcs` (reuse the DTO shape).
- Legion damage lands in `damageFinal` → contributes to participant damage & rewards (confirm).
- Tests (seeded RNG, mocked legion repos): no active legion → unchanged hit; active legion adds expected
  power; PowerScaling halves it; a unit ability proc fires/doesn't/caps; legion damage in `DamageDealt`
  and participant total; existing RaidService tests still pass (don't break mount/magic/crit assertions).
**Commit independently. DEEP review (formula order, PowerScaling, contribution, no double-count with mount/magic).**

## SLICE 5 — Commander slot  *(MODERATE)*

- The active legion has a Commander slot that holds an **equipment** item where **only procs apply**
  (stat/energy/stamina bonuses do NOT). Reuse the System 13 gear system: a dedicated commander equip
  endpoint that accepts a gear def, but combat only reads its proc (ignores BonusAttack/BonusDefense and
  any energy/stamina-type bonus). Persist as a slot on the active legion (or a `PlayerCommanderGear` row).
- Endpoints: `PUT /api/legions/commander` (equip), `DELETE /api/legions/commander`.
- Combat: commander gear proc fires in the proc phase (procs-only filter).
- Tests: equip; commander proc fires; commander stat bonuses are ignored in the hit.
**Commit independently. MODERATE review (the procs-only filter is the correctness point).**

## SLICE 6 — Economy / acquisition  *(MODERATE)*

- `ILegionService.GrantUnitAsync` / `GrantLegionAsync` (idempotent upsert).
- Loot drops: extend loot tables to grant units/legions (mirror System 14's `magicDrops`), wired into
  Raid/Quest reward distribution.
- Gem/Planet-Coin shop: `POST /api/units/buy`, `POST /api/legions/buy` — spend gems; ownership pre-check
  (reject AlreadyOwned without charging) and **idempotent** `referenceId` on the spend (learn from the
  System 14 BuyMagic bug — do NOT repeat it). Add `gemPrice` to the defs this slice.
- Tests: grant from loot; buy success; insufficient balance; **buy-twice charges once** (idempotency).
**Commit independently. MODERATE review (economy idempotency — same class as the magic money bug).**

---

## Constraints (every slice)

- Domain entities: private setters, no EF attributes; state via methods/factories.
- EF Fluent only, snake_case; every table `id`/`created_at`/`updated_at`/`is_deleted`; FKs indexed.
- Content providers singletons; throw at startup on invalid data.
- Controllers thin; `PlayerId` from JWT `sub`; server-authoritative.
- Do NOT run `dotnet ef database update`. Build 0 warnings; all tests green before committing a slice.
  Update `PROJECT_STATE.md` count + Function Reference as you go.
- No co-author trailer. One branch + one merge per slice; never bundle.
