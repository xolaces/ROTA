using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerLegionSlotRepository : IPlayerLegionSlotRepository
{
    private readonly RotaDbContext _db;

    public PlayerLegionSlotRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlayerLegionSlot>> GetForLegionAsync(
        Guid playerId, string legionDefinitionId, CancellationToken ct = default)
        => await _db.PlayerLegionSlots
            .Where(s => s.PlayerId == playerId
                     && s.LegionDefinitionId == legionDefinitionId
                     && !s.IsDeleted)
            .ToListAsync(ct);

    public Task<PlayerLegionSlot?> FindAsync(
        Guid playerId, string legionDefinitionId, LegionSlotFamily family, int slotIndex,
        CancellationToken ct = default)
        => _db.PlayerLegionSlots
            .FirstOrDefaultAsync(
                s => s.PlayerId           == playerId
                  && s.LegionDefinitionId == legionDefinitionId
                  && s.SlotFamily         == family
                  && s.SlotIndex          == slotIndex
                  && !s.IsDeleted, ct);

    public async Task UpsertAsync(
        Guid playerId, string legionDefinitionId, LegionSlotFamily family, int slotIndex,
        string unitDefinitionId, CancellationToken ct = default)
    {
        var existing = await FindAsync(playerId, legionDefinitionId, family, slotIndex, ct);
        if (existing is null)
        {
            _db.PlayerLegionSlots.Add(
                PlayerLegionSlot.Create(playerId, legionDefinitionId, family, slotIndex, unitDefinitionId));
        }
        else
        {
            existing.Reassign(unitDefinitionId);
            _db.PlayerLegionSlots.Update(existing);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(PlayerLegionSlot slot, CancellationToken ct = default)
    {
        slot.SoftDelete();
        _db.PlayerLegionSlots.Update(slot);
        await _db.SaveChangesAsync(ct);
    }
}
