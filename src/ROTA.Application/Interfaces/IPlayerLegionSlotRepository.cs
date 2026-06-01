using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

// BETA-PLACEHOLDER: stub for Slice 2 DI wiring. Full implementation added in Slice 3.
public interface IPlayerLegionSlotRepository
{
    Task<IReadOnlyList<PlayerLegionSlot>> GetForLegionAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default);
    Task<PlayerLegionSlot?> FindAsync(Guid playerId, string legionDefinitionId, LegionSlotFamily family, int slotIndex, CancellationToken ct = default);
    Task UpsertAsync(Guid playerId, string legionDefinitionId, LegionSlotFamily family, int slotIndex, string unitDefinitionId, CancellationToken ct = default);
    Task SoftDeleteAsync(PlayerLegionSlot slot, CancellationToken ct = default);
}
