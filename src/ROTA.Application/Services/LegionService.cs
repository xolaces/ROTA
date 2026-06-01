using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA
public sealed class LegionService : ILegionService
{
    private readonly IPlayerUnitRepository     _units;
    private readonly IPlayerLegionRepository   _legions;
    private readonly IUnitDefinitionProvider   _unitDefs;
    private readonly ILegionDefinitionProvider _legionDefs;

    public LegionService(
        IPlayerUnitRepository     units,
        IPlayerLegionRepository   legions,
        IUnitDefinitionProvider   unitDefs,
        ILegionDefinitionProvider legionDefs)
    {
        _units      = units;
        _legions    = legions;
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
    // Slice 3 — assembly + power computation (implemented in Slice 3)
    // ----------------------------------------------------------------

    public Task<SetActiveLegionResult>  SetActiveLegionAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default)
        => throw new NotImplementedException("Implemented in Slice 3.");

    public Task<AssignSlotResult> AssignSlotAsync(Guid playerId, string legionDefinitionId, string family, int slotIndex, string unitDefinitionId, CancellationToken ct = default)
        => throw new NotImplementedException("Implemented in Slice 3.");

    public Task<ClearSlotResult> ClearSlotAsync(Guid playerId, string legionDefinitionId, string family, int slotIndex, CancellationToken ct = default)
        => throw new NotImplementedException("Implemented in Slice 3.");

    public Task<LegionPowerResult> ComputeLegionPowerAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default)
        => throw new NotImplementedException("Implemented in Slice 3.");

    public Task<LegionDetailResponse?> GetLegionDetailAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default)
        => throw new NotImplementedException("Implemented in Slice 3.");
}
