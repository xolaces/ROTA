using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerEquipmentRepository : IPlayerEquipmentRepository
{
    private readonly RotaDbContext _db;

    public PlayerEquipmentRepository(RotaDbContext db) => _db = db;

    public Task<PlayerEquipment?> FindBySlotAsync(
        Guid playerId, EquipmentSlot slot, CancellationToken ct = default)
        => _db.PlayerEquipment
              .FirstOrDefaultAsync(e => e.PlayerId == playerId && e.Slot == slot, ct);

    public async Task<IReadOnlyList<PlayerEquipment>> GetEquippedAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var rows = await _db.PlayerEquipment
            .Where(e => e.PlayerId == playerId && !e.IsDeleted)
            .ToListAsync(ct);
        return rows;
    }

    public async Task CreateAsync(PlayerEquipment equipment, CancellationToken ct = default)
    {
        _db.PlayerEquipment.Add(equipment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PlayerEquipment equipment, CancellationToken ct = default)
    {
        _db.PlayerEquipment.Update(equipment);
        await _db.SaveChangesAsync(ct);
    }
}
