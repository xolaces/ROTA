using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerUnitRepository : IPlayerUnitRepository
{
    private readonly RotaDbContext _db;

    public PlayerUnitRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlayerUnit>> GetOwnedAsync(
        Guid playerId, CancellationToken ct = default)
        => await _db.PlayerUnits
            .Where(u => u.PlayerId == playerId && !u.IsDeleted)
            .ToListAsync(ct);

    public Task<PlayerUnit?> FindAsync(
        Guid playerId, string unitDefinitionId, CancellationToken ct = default)
        => _db.PlayerUnits
            .FirstOrDefaultAsync(
                u => u.PlayerId == playerId && u.UnitDefinitionId == unitDefinitionId, ct);

    public async Task<PlayerUnit> UpsertAsync(
        Guid playerId, string unitDefinitionId, CancellationToken ct = default)
    {
        var existing = await FindAsync(playerId, unitDefinitionId, ct);
        if (existing is null)
        {
            var newUnit = PlayerUnit.Create(playerId, unitDefinitionId);
            _db.PlayerUnits.Add(newUnit);
            await _db.SaveChangesAsync(ct);
            return newUnit;
        }

        if (existing.IsDeleted)
        {
            existing.Restore();
            _db.PlayerUnits.Update(existing);
            await _db.SaveChangesAsync(ct);
        }

        return existing;
    }
}
