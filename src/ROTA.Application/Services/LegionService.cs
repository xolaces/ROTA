using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA
public sealed class LegionService : ILegionService
{
    private readonly IPlayerUnitRepository       _units;
    private readonly IPlayerLegionRepository     _legions;
    private readonly IPlayerLegionSlotRepository _slots;
    private readonly IUnitDefinitionProvider     _unitDefs;
    private readonly ILegionDefinitionProvider   _legionDefs;

    public LegionService(
        IPlayerUnitRepository       units,
        IPlayerLegionRepository     legions,
        IPlayerLegionSlotRepository slots,
        IUnitDefinitionProvider     unitDefs,
        ILegionDefinitionProvider   legionDefs)
    {
        _units      = units;
        _legions    = legions;
        _slots      = slots;
        _unitDefs   = unitDefs;
        _legionDefs = legionDefs;
    }

    // ----------------------------------------------------------------
    // Slice 2 — ownership
    // ----------------------------------------------------------------

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

    // ----------------------------------------------------------------
    // Slice 3 — assembly + power computation
    // ----------------------------------------------------------------

    public async Task<SetActiveLegionResult> SetActiveLegionAsync(
        Guid playerId, string legionDefinitionId, CancellationToken ct = default)
    {
        var def = _legionDefs.GetById(legionDefinitionId);
        if (def is null)
            return new SetActiveLegionResult { FailureReason = "Legion definition not found." };

        var owned = await _legions.FindAsync(playerId, legionDefinitionId, ct);
        if (owned is null || owned.IsDeleted)
            return new SetActiveLegionResult { FailureReason = "You do not own this legion." };

        // Clear IsActive on all others, then set this one.
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
        if (unitOwned is null || unitOwned.IsDeleted)
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

            // DotD coefficients: General 2.0×ATK + 0.4×DEF; Troop 1.44×ATK + 0.36×DEF
            double atkCoeff = unitDef.UnitType == UnitType.General ? 2.0  : 1.44;
            double defCoeff = unitDef.UnitType == UnitType.General ? 0.4  : 0.36;
            unitSum += atkCoeff * unitDef.BaseAttack + defCoeff * unitDef.BaseDefense;

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

    // ----------------------------------------------------------------
    // HELPERS
    // ----------------------------------------------------------------

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
}
