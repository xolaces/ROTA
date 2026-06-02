using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface ILegionService
{
    // Slice 2 — ownership
    Task<IReadOnlyList<OwnedUnitResponse>>   GetOwnedUnitsAsync(Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<OwnedLegionResponse>> GetOwnedLegionsAsync(Guid playerId, CancellationToken ct = default);

    // Slice 3 — assembly + power
    Task<SetActiveLegionResult> SetActiveLegionAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default);
    Task<AssignSlotResult>      AssignSlotAsync(Guid playerId, string legionDefinitionId, string family, int slotIndex, string unitDefinitionId, CancellationToken ct = default);
    Task<ClearSlotResult>       ClearSlotAsync(Guid playerId, string legionDefinitionId, string family, int slotIndex, CancellationToken ct = default);
    Task<LegionPowerResult>     ComputeLegionPowerAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default);
    Task<LegionDetailResponse?> GetLegionDetailAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default);

    // Slice 5 — commander slot
    Task<CommanderEquipResult>   EquipCommanderAsync(Guid playerId, string gearDefinitionId, CancellationToken ct = default);
    Task<CommanderUnequipResult> UnequipCommanderAsync(Guid playerId, CancellationToken ct = default);
    Task<CommanderGearResponse?> GetCommanderAsync(Guid playerId, CancellationToken ct = default);
}
