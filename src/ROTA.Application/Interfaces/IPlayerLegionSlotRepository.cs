using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

// FINAL (System 15 Slice 3) — per-legion slot assignments. (Was a Slice-2 placeholder stub;
// fully implemented by PlayerLegionSlotRepository since v0.2.7.)
public interface IPlayerLegionSlotRepository
{
    Task<IReadOnlyList<PlayerLegionSlot>> GetForLegionAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default);
    Task<PlayerLegionSlot?> FindAsync(Guid playerId, string legionDefinitionId, LegionSlotFamily family, int slotIndex, CancellationToken ct = default);
    Task UpsertAsync(Guid playerId, string legionDefinitionId, LegionSlotFamily family, int slotIndex, string unitDefinitionId, CancellationToken ct = default);
    Task SoftDeleteAsync(PlayerLegionSlot slot, CancellationToken ct = default);
}
