using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerInventoryRepository : IPlayerInventoryRepository
{
    private readonly RotaDbContext _db;

    public PlayerInventoryRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlayerInventoryItem>> GetAllForPlayerAsync(
        Guid playerId, CancellationToken ct = default)
        => await _db.PlayerInventoryItems
            .Where(i => i.PlayerId == playerId)
            .ToListAsync(ct);

    public async Task<PlayerInventoryItem?> GetAsync(
        Guid playerId, string itemDefinitionId, CancellationToken ct = default)
        => await _db.PlayerInventoryItems
            .FirstOrDefaultAsync(i => i.PlayerId == playerId
                                   && i.ItemDefinitionId == itemDefinitionId, ct);

    public async Task CreateAsync(PlayerInventoryItem item, CancellationToken ct = default)
    {
        _db.PlayerInventoryItems.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PlayerInventoryItem item, CancellationToken ct = default)
    {
        _db.PlayerInventoryItems.Update(item);
        await _db.SaveChangesAsync(ct);
    }
}
