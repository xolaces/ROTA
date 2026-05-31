# System 13 — Conditional / Stacking Bonuses (v0.2.5)

*Status: LOCKED — build against this spec exactly.*

## Goal

A reusable, content-driven bonus framework so gear **and future troops/legions** can declare
"for every X owned, gain Y" with **no C# changes** — JSON only. All bonus math is server-side
at combat time, never client-trusted. Proved against existing inventory; troop-targeting lights
up when legions land.

---

## Design decisions (resolved)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| `ProcChanceFlat` clamp | ≤ 1.0 (100% proc max) | Already locked |
| `FlatDamagePercent` ceiling | None | Content-controlled via JSON; troops don't exist yet; add when content warrants it |
| `EffectiveCombatData` shape | Flat field `double FlatDamagePercent` | One extra field; sub-object is premature with one escapee |
| Eval timing | Per-hit | Inventory reads are indexed/sub-ms; caching adds invalidation complexity |

---

## JSON contract

Attached to any gear or troop definition:

```json
"conditionalBonuses": [
  {
    "conditionType": "OwnedUnitCount",
    "conditionTarget": "troop_farmhand",
    "perCount": 10,
    "bonusType": "ProcChanceFlat",
    "bonusAmount": 0.05
  }
]
```

Eval rule: `floor(owned / perCount) × bonusAmount`, accumulated per `bonusType`.

---

## New types

### `src/ROTA.Application/Models/ConditionalBonus.cs`

```
ConditionType  enum  OwnedUnitCount | OwnedTypeCount | EquippedSlot
BonusType      enum  FlatAttack | FlatDefense | ProcChanceFlat | ProcAmountFlat | FlatDamagePercent
ConditionalBonus class  { ConditionType, ConditionTarget, PerCount, BonusType, BonusAmount }
```

- `OwnedUnitCount`: `conditionTarget` = specific item/unit definition ID; `floor(qty / perCount)`
- `OwnedTypeCount`: `conditionTarget` = item tag string; total qty across all matching items; `floor(total / perCount)`
- `EquippedSlot`: binary (0 or 1); `conditionTarget` = slot name; `perCount = 1` (always)

### `src/ROTA.Application/Services/ConditionalBonusEvaluator.cs`

Static class + record:

```
AccumulatedBonuses record(int FlatAttack, int FlatDefense,
                          double ProcChanceFlat, double ProcAmountFlat,
                          double FlatDamagePercent)

ConditionalBonusEvaluator.Evaluate(
    IEnumerable<ConditionalBonus> bonuses,
    IReadOnlyDictionary<string, int> ownedById,   // itemDefId → quantity
    IReadOnlyDictionary<string, int> ownedByTag,  // tag → total quantity
    IReadOnlySet<string> equippedSlots)            // lower-invariant slot names
→ AccumulatedBonuses
```

No ceiling applied inside the evaluator — raw sums only. Clamps applied at use sites.

---

## Modified types

### `GearDefinition`
Add: `public List<ConditionalBonus> ConditionalBonuses { get; set; } = new();`

### `ItemDefinition`
Add: `public List<string> Tags { get; set; } = new();`
Tags enable `OwnedTypeCount` lookups (e.g. `"material"`, `"sigil"`).

### `EffectiveCombatData` (IEquipmentService.cs)
```csharp
public sealed record EffectiveCombatData(
    int           EffectiveAttack,
    int           EffectiveDefense,
    GearProcData? MountProc,
    double        FlatDamagePercent);   // 0.0 when no conditional bonuses
```

---

## Pipeline seam — `EquipmentService.GetEffectiveCombatDataAsync`

New ctor params: `IPlayerInventoryRepository inventory`, `IItemDefinitionProvider itemDefs`

Evaluation steps (all per-hit, inside the method):

1. Load equipped rows: `_repo.GetEquippedAsync(playerId, ct)`
2. Load inventory: `_inventory.GetAllForPlayerAsync(playerId, ct)`
3. Build `ownedById`: `inventory.ToDictionary(i => i.ItemDefinitionId, i => i.Quantity)`
4. Build `ownedByTag`: for each inventory item, look up its definition tags; accumulate `tag → total qty`
5. Build `equippedSlots`: `equippedRows.Select(r => r.Slot.ToString().ToLowerInvariant()).ToHashSet()`
6. Collect all `ConditionalBonuses` from all equipped gear definitions
7. Call `ConditionalBonusEvaluator.Evaluate(...)`
8. `bonusAtk += evaluated.FlatAttack`; `bonusDef += evaluated.FlatDefense`
9. If mount proc exists: `adjustedChance = Math.Min(1.0, mountBase.ProcChance + evaluated.ProcChanceFlat)`;
   `adjustedAmount = mountBase.ProcPercent + evaluated.ProcAmountFlat`
10. Return `EffectiveCombatData(baseAtk + bonusAtk, baseDef + bonusDef, adjustedProc, evaluated.FlatDamagePercent)`

---

## Pipeline seam — `RaidService.HitRaidAsync`

After crit, before applying damage to raid HP:

```csharp
if (combat.FlatDamagePercent > 0)
    damageFinal = Math.Max(1, (long)(damageFinal * (1.0 + combat.FlatDamagePercent)));
```

Order: base → proc → crit → **FlatDamagePercent** → TakeDamage.

---

## Debt fold-in A — Reward atomicity

**Problem:** stamina spend uses its own `AtomicUpdateAsync` transaction outside the advisory lock.
A crash between spend and lock acquisition loses stamina permanently.

**Fix:** Make `PlayerResourceRepository.AtomicUpdateAsync` detect an ambient transaction and reuse it
instead of opening a new one. Then move `SpendEnergyAsync` INSIDE the `AtomicApplyHitAsync` callback,
placing stamina deduction in the same PostgreSQL transaction as the hit.

### `PlayerResourceRepository.AtomicUpdateAsync`
```csharp
bool ownTx = _db.Database.CurrentTransaction is null;
IDbContextTransaction? tx = ownTx ? await _db.Database.BeginTransactionAsync(ct) : null;
try
{
    // FOR UPDATE + updateFn + SaveChangesAsync (same as before)
    if (ownTx && tx is not null) await tx.CommitAsync(ct);
    return true;
}
catch { if (ownTx) await tx?.RollbackAsync(ct); throw; }
finally { if (ownTx) tx?.DisposeAsync(); }
```

### `RaidService.HitRaidAsync` new flow

```
1-5. Pre-checks (expiry, defeated, access, cap, idempotency, hit size validation)
6.   Compute staminaCost (before callback — definition already loaded)
7.   AtomicApplyHitAsync callback:
     a. Re-check defeated/expired → raceCondition captured variable
     b. SpendEnergyAsync (inside tx) → staminaInsufficient captured on false
     c. FindByIdWithStatsAsync (moved inside — only needed after successful spend)
     d. GetEffectiveCombatDataAsync
     e. Damage formula (base → proc → crit → FlatDamagePercent)
     f. TakeDamage, upsert participant
     g. On-hit XP/gold
     h. Kill rewards
8.   !applied handler: staminaInsufficient → InsufficientStamina;
     raceCondition → RaidAlreadyDefeated; else RaidNotFound.
     NO refund path — rollback is automatic.
```

The audit `RaidHitRefund` log entry is removed (rollback makes it impossible to reach).

---

## Debt fold-in B — ProcBonus type

`RaidHitResponse.ProcBonus`: change `public double ProcBonus` → `public long ProcBonus`.

---

## Test plan

### New test class: `ConditionalBonusEvaluatorTests`
1. `Evaluate_OwnedUnitCount_FloorDivision` — owns 25, perCount=10 → 2 stacks; FlatAttack=5 → +10
2. `Evaluate_OwnedUnitCount_ZeroOwned_NoBonus`
3. `Evaluate_OwnedUnitCount_BelowThreshold_NoBonus` — owns 5, perCount=10 → 0 stacks
4. `Evaluate_OwnedTypeCount_AccumulatesAcrossItems` — 3 of tag A + 2 of tag A = 5 total, perCount=5 → 1 stack
5. `Evaluate_EquippedSlot_Occupied_GivesBonus` — equippedSlots contains "mount" → 1 stack
6. `Evaluate_EquippedSlot_Empty_NoBonus`
7. `Evaluate_MultipleBonusTypes_AllAccumulated` — mix of all 5 types in one call
8. `Evaluate_EmptyBonusList_ReturnsZeros`

### Updated `EquipmentServiceTests` (broken by ctor + EffectiveCombatData changes)
- `BuildService()` — add inventory mock (returns empty list) + itemDefs mock
- Existing tests: add `InventoryRepository` mock setup returning `[]`; GearDefinition objects already default to empty `ConditionalBonuses`

New tests:
9. `GetEffectiveCombatData_ConditionalFlatAttack_AddsToEffectiveAttack` — gear with OwnedUnitCount FlatAttack, inventory has enough → bonus applied
10. `GetEffectiveCombatData_ConditionalProcChanceClamped_NeverExceedsOne` — base 0.8 + conditional 0.5 → clamped to 1.0
11. `GetEffectiveCombatData_FlatDamagePercent_PropagatesInResult`
12. `GetEffectiveCombatData_TagBasedBonus_AccumulatesAcrossItems`

### Updated `RaidServiceTests`
- `BuildService` default `EffectiveCombatData` mock: `new EffectiveCombatData(atk, def, null, 0.0)`
- Proc tests: `new EffectiveCombatData(10, 10, new GearProcData(0.05, 2.0), 0.0)`
- `Hit_ReturnsInsufficientStamina_HpUnchanged`: add `AtomicApplyHitAsync` setup (callback invokes mutate)

New test:
13. `Hit_FlatDamagePercent_IncreasesBaseDamage` — equipment mock returns FlatDamagePercent=0.5 → damage ≥ 1.5× base floor

### Updated `RaidConcurrencyTests`
- Update assertion comment: "loser's stamina rolled back atomically — net zero"

---

## Function Reference refresh (debt fold-in C)

Full refresh of `docs/ROTA_Function_Reference.md`:
- `IClassService` — mark as COMPLETE (no longer "to be added")
- Add `EquipmentService` + `IEquipmentService` entries
- Add `ConditionalBonusEvaluator` entry
- Add new entity/enum sections for `ConditionType`, `BonusType`, `ConditionalBonus`
- Add `PlayerEquipment` entity to entity table
- Remove stale `*(to be added — Section B)*` annotations
- Update Phase 2 backlog (remove stamina atomicity — now resolved)

---

## Content example (gear.json)

Starter gear has no `conditionalBonuses` — omit the field (defaults to `[]`). The framework
is proven; content with bonuses can be added without C# changes.

To demonstrate the contract works (but not ship as gameplay-tuned values):
```json
{
  "id": "gear_farmhand_signet",
  "name": "Farmhand's Signet",
  "description": "Grows stronger with your workforce.",
  "rarity": "Green",
  "slot": "Ring1",
  "bonusAttack": 0,
  "bonusDefense": 0,
  "conditionalBonuses": [
    { "conditionType": "OwnedUnitCount", "conditionTarget": "item_farmhand_unit",
      "perCount": 10, "bonusType": "FlatAttack", "bonusAmount": 1 }
  ]
}
```
*Do NOT add this to gear.json — it references a unit that doesn't exist yet. The example is
spec-only. The framework proves out against existing items (sigils, materials) which have no
conditional bonuses — all evaluations return zeros, preserving v0.2.4 behavior exactly.*
