using Microsoft.Extensions.Options;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA
public sealed class LegionService : ILegionService
{
    private readonly IPlayerUnitRepository          _units;
    private readonly IPlayerLegionRepository        _legions;
    private readonly IPlayerLegionSlotRepository    _slots;
    private readonly IUnitDefinitionProvider        _unitDefs;
    private readonly ILegionDefinitionProvider      _legionDefs;
    private readonly IPlayerCommanderGearRepository _commanderGear;
    private readonly IGearDefinitionProvider        _gearDefs;
    private readonly IPlayerGearRepository          _gearRepo;   // exploit audit 2026-06-14 (C): commander ownership gate
    private readonly IGemService                    _gems;
    private readonly LegionConfig                   _legionConfig;

    public LegionService(
        IPlayerUnitRepository          units,
        IPlayerLegionRepository        legions,
        IPlayerLegionSlotRepository    slots,
        IUnitDefinitionProvider        unitDefs,
        ILegionDefinitionProvider      legionDefs,
        IPlayerCommanderGearRepository commanderGear,
        IGearDefinitionProvider        gearDefs,
        IPlayerGearRepository          gearRepo,
        IGemService                    gems,
        IOptions<LegionConfig>         legionConfig)
    {
        _units         = units;
        _legions       = legions;
        _slots         = slots;
        _unitDefs      = unitDefs;
        _legionDefs    = legionDefs;
        _commanderGear = commanderGear;
        _gearDefs      = gearDefs;
        _gearRepo      = gearRepo;
        _gems          = gems;
        _legionConfig  = legionConfig.Value;
    }

    public async Task<IReadOnlyList<OwnedUnitResponse>> GetOwnedUnitsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var rows   = await _units.GetOwnedAsync(playerId, ct);
        var result = new List<OwnedUnitResponse>(rows.Count);
        foreach (var row in rows)
        {
            var def = _unitDefs.GetById(row.UnitDefinitionId);
            if (def is null) continue;
            result.Add(new OwnedUnitResponse
            {
                UnitDefinitionId = def.Id,
                Name             = def.Name,
                Description      = def.Description,
                UnitType         = def.UnitType.ToString(),
                Rarity           = def.Rarity.ToString(),
                BaseAttack       = def.BaseAttack,
                BaseDefense      = def.BaseDefense,
                Race             = def.Race.ToString(),
                Role             = def.Role.ToString(),
                Attribute        = def.Attribute.ToString(),
                HasAbility       = def.Ability is not null,
                LegionBonus      = def.LegionBonus,
                IconPath         = def.IconPath,
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<OwnedLegionResponse>> GetOwnedLegionsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var rows   = await _legions.GetOwnedAsync(playerId, ct);
        var result = new List<OwnedLegionResponse>(rows.Count);
        foreach (var row in rows)
        {
            var def = _legionDefs.GetById(row.LegionDefinitionId);
            if (def is null) continue;
            result.Add(new OwnedLegionResponse
            {
                LegionDefinitionId = def.Id,
                Name               = def.Name,
                Description        = def.Description,
                Rarity             = def.Rarity.ToString(),
                PowerBonus         = def.PowerBonus,
                GeneralSlotCount   = def.GeneralSlots.Count,
                TroopSlotCount     = def.TroopSlots.Count,
                IsActive           = row.IsActive,
                IconPath           = def.IconPath,
            });
        }
        return result;
    }

    public async Task<SetActiveLegionResult> SetActiveLegionAsync(
        Guid playerId, string legionDefinitionId, CancellationToken ct = default)
    {
        var def = _legionDefs.GetById(legionDefinitionId);
        if (def is null)
            return new SetActiveLegionResult { FailureReason = "Legion definition not found." };

        var owned = await _legions.FindAsync(playerId, legionDefinitionId, ct);
        if (owned is null || owned.IsDeleted)
            return new SetActiveLegionResult { FailureReason = "You do not own this legion." };

        var allOwned = await _legions.GetOwnedAsync(playerId, ct);
        foreach (var other in allOwned.Where(l => l.IsActive && l.LegionDefinitionId != legionDefinitionId))
        {
            other.SetActive(false);
            await _legions.UpdateAsync(other, ct);
        }

        if (!owned.IsActive)
        {
            owned.SetActive(true);
            await _legions.UpdateAsync(owned, ct);
        }

        return new SetActiveLegionResult { Success = true };
    }

    public async Task<AssignSlotResult> AssignSlotAsync(
        Guid playerId, string legionDefinitionId, string family, int slotIndex,
        string unitDefinitionId, CancellationToken ct = default)
    {
        if (!Enum.TryParse<LegionSlotFamily>(family, ignoreCase: true, out var slotFamily))
            return SlotFail(AssignSlotFailureCode.SlotOutOfRange, $"Unknown slot family '{family}'.");

        var legionDef = _legionDefs.GetById(legionDefinitionId);
        if (legionDef is null)
            return SlotFail(AssignSlotFailureCode.LegionNotOwned, "Legion definition not found.");

        var legionOwned = await _legions.FindAsync(playerId, legionDefinitionId, ct);
        if (legionOwned is null || legionOwned.IsDeleted)
            return SlotFail(AssignSlotFailureCode.LegionNotOwned, "You do not own this legion.");

        var specList = slotFamily == LegionSlotFamily.General
            ? legionDef.GeneralSlots
            : legionDef.TroopSlots;
        if (slotIndex < 0 || slotIndex >= specList.Count)
            return SlotFail(AssignSlotFailureCode.SlotOutOfRange,
                $"Slot index {slotIndex} out of range for family {family} (0–{specList.Count - 1}).");

        var unitDef = _unitDefs.GetById(unitDefinitionId);
        if (unitDef is null)
            return SlotFail(AssignSlotFailureCode.UnitNotOwned, "Unit definition not found.");

        var unitOwned = await _units.FindAsync(playerId, unitDefinitionId, ct);
        // Fold-in (auditor Slice 3 review): verify both ownership and non-zero quantity.
        if (unitOwned is null || unitOwned.IsDeleted || unitOwned.Quantity < 1)
            return SlotFail(AssignSlotFailureCode.UnitNotOwned, "You do not own this unit.");

        var expectedUnitType = slotFamily == LegionSlotFamily.General ? UnitType.General : UnitType.Troop;
        if (unitDef.UnitType != expectedUnitType)
            return SlotFail(AssignSlotFailureCode.WrongUnitType,
                $"Slot requires a {expectedUnitType}; '{unitDefinitionId}' is a {unitDef.UnitType}.");

        var spec          = specList[slotIndex];
        var constraintErr = CheckConstraint(spec, unitDef, unitDefinitionId);
        if (constraintErr is not null) return constraintErr;

        // Unit must not already fill another slot in this same legion.
        var existingSlots = await _slots.GetForLegionAsync(playerId, legionDefinitionId, ct);
        bool duplicateInOtherSlot = existingSlots.Any(s =>
            s.UnitDefinitionId == unitDefinitionId
            && !(s.SlotFamily == slotFamily && s.SlotIndex == slotIndex));
        if (duplicateInOtherSlot)
            return SlotFail(AssignSlotFailureCode.UnitAlreadyAssigned,
                "This unit is already assigned to another slot in this legion.");

        await _slots.UpsertAsync(playerId, legionDefinitionId, slotFamily, slotIndex, unitDefinitionId, ct);
        return new AssignSlotResult { Success = true };
    }

    public async Task<ClearSlotResult> ClearSlotAsync(
        Guid playerId, string legionDefinitionId, string family, int slotIndex,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<LegionSlotFamily>(family, ignoreCase: true, out var slotFamily))
            return new ClearSlotResult { FailureReason = $"Unknown slot family '{family}'." };

        var existing = await _slots.FindAsync(playerId, legionDefinitionId, slotFamily, slotIndex, ct);
        if (existing is null)
            return new ClearSlotResult { Success = true }; // already empty — idempotent

        await _slots.SoftDeleteAsync(existing, ct);
        return new ClearSlotResult { Success = true };
    }

    public async Task<LegionPowerResult> ComputeLegionPowerAsync(
        Guid playerId, string legionDefinitionId, CancellationToken ct = default)
    {
        var def = _legionDefs.GetById(legionDefinitionId)
            ?? throw new InvalidOperationException(
                $"Legion definition '{legionDefinitionId}' not found.");

        var assignedSlots = await _slots.GetForLegionAsync(playerId, legionDefinitionId, ct);

        double unitSum          = 0;
        double totalLegionBonus = def.PowerBonus;   // % from legion def itself

        foreach (var slot in assignedSlots)
        {
            var unitDef = _unitDefs.GetById(slot.UnitDefinitionId);
            if (unitDef is null) continue;

            // Fold-in (auditor Slice 3 review): use LegionConfig.UnitCoefficients instead of
            // hardcoded values so display power cannot drift from combat power (RaidService
            // already reads from config). Fall back to Troop defaults if key missing.
            var coeffKey = unitDef.UnitType == UnitType.General ? "General" : "Troop";
            var coeffs   = _legionConfig.UnitCoefficients.TryGetValue(coeffKey, out var c)
                ? c : new UnitCoefficients { Atk = 1.44, Def = 0.36 };
            unitSum += coeffs.Atk * unitDef.BaseAttack + coeffs.Def * unitDef.BaseDefense;

            if (unitDef.UnitType == UnitType.General)
                totalLegionBonus += unitDef.LegionBonus;
        }

        double bonusFraction = totalLegionBonus / 100.0;
        // No PowerScaling here — that's a combat-only dial applied in RaidService (Slice 4).
        double rawPower = unitSum * (1.0 + bonusFraction);

        return new LegionPowerResult
        {
            RawPower            = rawPower,
            LegionBonusFraction = bonusFraction,
            UnitSum             = unitSum,
        };
    }

    public async Task<LegionDetailResponse?> GetLegionDetailAsync(
        Guid playerId, string legionDefinitionId, CancellationToken ct = default)
    {
        var def = _legionDefs.GetById(legionDefinitionId);
        if (def is null) return null;

        var owned = await _legions.FindAsync(playerId, legionDefinitionId, ct);
        if (owned is null || owned.IsDeleted) return null;

        var assignedSlots = await _slots.GetForLegionAsync(playerId, legionDefinitionId, ct);
        var slotMap = assignedSlots.ToDictionary(
            s => (s.SlotFamily, s.SlotIndex), s => s.UnitDefinitionId);

        var slotResponses = new List<SlotAssignmentResponse>();
        AddSlots(def.GeneralSlots, LegionSlotFamily.General, slotMap, slotResponses);
        AddSlots(def.TroopSlots,   LegionSlotFamily.Troop,   slotMap, slotResponses);

        var power = await ComputeLegionPowerAsync(playerId, legionDefinitionId, ct);

        return new LegionDetailResponse
        {
            LegionDefinitionId = def.Id,
            Name               = def.Name,
            IsActive           = owned.IsActive,
            PowerBonus         = def.PowerBonus,
            Slots              = slotResponses,
            ComputedPower      = power,
        };
    }

    public async Task<CommanderEquipResult> EquipCommanderAsync(
        Guid playerId, string gearDefinitionId, CancellationToken ct = default)
    {
        var def = _gearDefs.GetById(gearDefinitionId);
        if (def is null)
            return new CommanderEquipResult
            {
                FailureCode   = CommanderEquipFailureCode.GearDefinitionNotFound,
                FailureReason = $"Gear definition '{gearDefinitionId}' not found.",
            };

        // SECURITY (exploit audit 2026-06-14, finding C): require ownership before equipping a commander
        // — mirrors EquipmentService.EquipAsync. Without this, any player could equip an unowned Orange
        // mount and gain a permanent, uncapped +procPercent×preProc combat proc for free.
        var owned = await _gearRepo.GetAsync(playerId, gearDefinitionId, ct);
        if (owned is null || owned.IsDeleted || owned.Quantity < 1)
            return new CommanderEquipResult
            {
                FailureCode   = CommanderEquipFailureCode.NotOwned,
                FailureReason = $"You do not own '{def.Name}'.",
            };

        var existing = await _commanderGear.FindAsync(playerId, ct);
        if (existing is null)
        {
            var row = PlayerCommanderGear.Create(playerId, gearDefinitionId);
            await _commanderGear.CreateAsync(row, ct);
        }
        else
        {
            // Equip() restores a soft-deleted row and updates the gear def.
            existing.Equip(gearDefinitionId);
            await _commanderGear.UpdateAsync(existing, ct);
        }

        return new CommanderEquipResult
        {
            Success          = true,
            GearDefinitionId = gearDefinitionId,
            GearName         = def.Name,
        };
    }

    public async Task<CommanderUnequipResult> UnequipCommanderAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var existing = await _commanderGear.FindAsync(playerId, ct);
        if (existing is null || existing.IsDeleted)
            return new CommanderUnequipResult { Success = true }; // idempotent

        existing.Unequip();
        await _commanderGear.UpdateAsync(existing, ct);
        return new CommanderUnequipResult { Success = true };
    }

    public async Task<CommanderGearResponse?> GetCommanderAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var row = await _commanderGear.FindAsync(playerId, ct);
        if (row is null || row.IsDeleted) return null;

        var def = _gearDefs.GetById(row.GearDefinitionId);
        if (def is null) return null;

        return new CommanderGearResponse
        {
            GearDefinitionId = def.Id,
            GearName         = def.Name,
            GearDescription  = def.Description,
            Rarity           = def.Rarity.ToString(),
            ProcChance       = def.ProcChance,
            ProcPercent      = def.ProcPercent,
        };
    }

    /// <summary>Idempotent unit grant — re-grant of an already-owned unit is a silent no-op.</summary>
    public async Task GrantUnitAsync(Guid playerId, string unitDefinitionId, CancellationToken ct = default)
        => await _units.UpsertAsync(playerId, unitDefinitionId, ct);

    /// <summary>Idempotent legion grant — re-grant of an already-owned legion is a silent no-op.</summary>
    public async Task GrantLegionAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default)
        => await _legions.UpsertAsync(playerId, legionDefinitionId, ct);

    public async Task<BuyUnitResult> BuyUnitAsync(
        Guid playerId, string unitDefinitionId, CancellationToken ct = default)
    {
        var def = _unitDefs.GetById(unitDefinitionId);
        if (def is null)
            return BuyUnitFail(BuyFailureCode.DefinitionNotFound, "Unit definition not found.");

        if (def.GemPrice <= 0)
            return BuyUnitFail(BuyFailureCode.NotForSale, "This unit is not available in the gem shop.");

        // Ownership pre-check: reject without charging (mirrors v0.2.6.1 BuyMagic fix).
        var existing = await _units.FindAsync(playerId, unitDefinitionId, ct);
        if (existing is not null && !existing.IsDeleted)
            return BuyUnitFail(BuyFailureCode.AlreadyOwned, "You already own this unit.");

        // Tri-state gem spend: Charged = first-time purchase; AlreadyProcessed = ledger row
        // already exists (crash-recovery replay — the charge committed but the grant was lost).
        // Both outcomes proceed with UpsertAsync (idempotent) so the player receives the unit
        // on retry without being double-charged. InsufficientBalance → reject.
        //
        // PHASE-2: wrap SpendGemsAsync + UpsertAsync in a single DB transaction so a mid-air
        // crash cannot produce the AlreadyProcessed state at all. Requires an
        // ITransactionScope or similar abstraction to share a transaction across repos without
        // leaking DbContext into Application. The current AlreadyProcessed path already handles
        // crash recovery correctly; atomicity is a hardening step, not a correctness fix.
        var refId   = $"unitbuy:{playerId}:{unitDefinitionId}";
        var outcome = await _gems.SpendGemsAsync(
            playerId, def.GemPrice, GemTransactionType.UnitPurchase, refId, ct);
        if (outcome == GemSpendOutcome.InsufficientBalance)
            return BuyUnitFail(BuyFailureCode.InsufficientGems,
                $"Insufficient gems. Required: {def.GemPrice}.");

        // outcome is Charged or AlreadyProcessed — proceed with (idempotent) grant.
        await _units.UpsertAsync(playerId, unitDefinitionId, ct);

        return new BuyUnitResult
        {
            Success          = true,
            UnitDefinitionId = def.Id,
            UnitName         = def.Name,
            GemsSpent        = def.GemPrice,
        };
    }

    public async Task<BuyLegionResult> BuyLegionAsync(
        Guid playerId, string legionDefinitionId, CancellationToken ct = default)
    {
        var def = _legionDefs.GetById(legionDefinitionId);
        if (def is null)
            return BuyLegionFail(BuyFailureCode.DefinitionNotFound, "Legion definition not found.");

        if (def.GemPrice <= 0)
            return BuyLegionFail(BuyFailureCode.NotForSale, "This legion is not available in the gem shop.");

        // Ownership pre-check: reject without charging.
        var existing = await _legions.FindAsync(playerId, legionDefinitionId, ct);
        if (existing is not null && !existing.IsDeleted)
            return BuyLegionFail(BuyFailureCode.AlreadyOwned, "You already own this legion.");

        // Tri-state gem spend: Charged = first-time purchase; AlreadyProcessed = ledger row
        // already exists (crash-recovery replay — the charge committed but the grant was lost).
        // Both outcomes proceed with UpsertAsync (idempotent) so the player receives the legion
        // on retry without being double-charged. InsufficientBalance → reject.
        //
        // PHASE-2: wrap SpendGemsAsync + UpsertAsync in a single DB transaction (see BuyUnitAsync).
        var refId   = $"legionbuy:{playerId}:{legionDefinitionId}";
        var outcome = await _gems.SpendGemsAsync(
            playerId, def.GemPrice, GemTransactionType.LegionPurchase, refId, ct);
        if (outcome == GemSpendOutcome.InsufficientBalance)
            return BuyLegionFail(BuyFailureCode.InsufficientGems,
                $"Insufficient gems. Required: {def.GemPrice}.");

        // outcome is Charged or AlreadyProcessed — proceed with (idempotent) grant.
        await _legions.UpsertAsync(playerId, legionDefinitionId, ct);

        return new BuyLegionResult
        {
            Success            = true,
            LegionDefinitionId = def.Id,
            LegionName         = def.Name,
            GemsSpent          = def.GemPrice,
        };
    }

    private void AddSlots(
        IList<SlotSpec> specs, LegionSlotFamily family,
        Dictionary<(LegionSlotFamily, int), string> slotMap,
        List<SlotAssignmentResponse> output)
    {
        for (int i = 0; i < specs.Count; i++)
        {
            var spec    = specs[i];
            var unitId  = slotMap.TryGetValue((family, i), out var uid) ? uid : null;
            var unitDef = unitId is not null ? _unitDefs.GetById(unitId) : null;
            output.Add(new SlotAssignmentResponse
            {
                Family           = family.ToString(),
                SlotIndex        = i,
                ConstraintType   = spec.ConstraintType.ToString(),
                ConstraintValue  = spec.ConstraintValue,
                UnitDefinitionId = unitId,
                UnitName         = unitDef?.Name,
            });
        }
    }

    private static AssignSlotResult? CheckConstraint(
        SlotSpec spec, UnitDefinition unit, string unitId)
    {
        if (spec.ConstraintType == SlotConstraintType.None || spec.ConstraintValue is null)
            return null;

        bool passes = spec.ConstraintType switch
        {
            SlotConstraintType.Race =>
                Enum.TryParse<UnitRace>(spec.ConstraintValue, out var race) && unit.Race == race,
            SlotConstraintType.Role =>
                Enum.TryParse<UnitRole>(spec.ConstraintValue, out var role) && unit.Role == role,
            SlotConstraintType.Attribute =>
                Enum.TryParse<UnitAttribute>(spec.ConstraintValue, out var attr) && unit.Attribute == attr,
            _ => false,
        };

        if (!passes)
        {
            string actualValue = spec.ConstraintType switch
            {
                SlotConstraintType.Race      => unit.Race.ToString(),
                SlotConstraintType.Role      => unit.Role.ToString(),
                SlotConstraintType.Attribute => unit.Attribute.ToString(),
                _                            => "?",
            };
            return SlotFail(AssignSlotFailureCode.ConstraintMismatch,
                $"Slot requires {spec.ConstraintType}={spec.ConstraintValue}; '{unitId}' has {spec.ConstraintType}={actualValue}.");
        }

        return null;
    }

    private static AssignSlotResult SlotFail(AssignSlotFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };

    private static BuyUnitResult BuyUnitFail(BuyFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };

    private static BuyLegionResult BuyLegionFail(BuyFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
