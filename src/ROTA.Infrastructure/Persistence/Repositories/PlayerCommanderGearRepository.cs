using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerCommanderGearRepository : IPlayerCommanderGearRepository
{
    private readonly RotaDbContext _db;

    public PlayerCommanderGearRepository(RotaDbContext db) => _db = db;

    /// <inheritdoc/>
    public Task<PlayerCommanderGear?> FindAsync(Guid playerId, CancellationToken ct = default)
        => _db.PlayerCommanderGear
               .IgnoreQueryFilters()
               .FirstOrDefaultAsync(r => r.PlayerId == playerId, ct);

    public async Task<PlayerCommanderGear> CreateAsync(PlayerCommanderGear row, CancellationToken ct = default)
    {
        _db.PlayerCommanderGear.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public async Task UpdateAsync(PlayerCommanderGear row, CancellationToken ct = default)
    {
        _db.PlayerCommanderGear.Update(row);
        await _db.SaveChangesAsync(ct);
    }
}
