using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerGearRepository : IPlayerGearRepository
{
    private readonly RotaDbContext _db;

    public PlayerGearRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlayerGear>> GetOwnedAsync(
        Guid playerId, CancellationToken ct = default)
        => await _db.PlayerGear
            .Where(g => g.PlayerId == playerId && !g.IsDeleted && g.Quantity > 0)
            .ToListAsync(ct);

    public async Task<PlayerGear?> GetAsync(
        Guid playerId, string gearDefinitionId, CancellationToken ct = default)
        => await _db.PlayerGear
            .FirstOrDefaultAsync(g => g.PlayerId == playerId
                                   && g.GearDefinitionId == gearDefinitionId, ct);

    public async Task CreateAsync(PlayerGear gear, CancellationToken ct = default)
    {
        _db.PlayerGear.Add(gear);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PlayerGear gear, CancellationToken ct = default)
    {
        _db.PlayerGear.Update(gear);
        await _db.SaveChangesAsync(ct);
    }
}
