using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerLegionRepository : IPlayerLegionRepository
{
    private readonly RotaDbContext _db;

    public PlayerLegionRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlayerLegion>> GetOwnedAsync(
        Guid playerId, CancellationToken ct = default)
        => await _db.PlayerLegions
            .Where(l => l.PlayerId == playerId && !l.IsDeleted)
            .ToListAsync(ct);

    public Task<PlayerLegion?> FindAsync(
        Guid playerId, string legionDefinitionId, CancellationToken ct = default)
        => _db.PlayerLegions
            .FirstOrDefaultAsync(
                l => l.PlayerId == playerId && l.LegionDefinitionId == legionDefinitionId, ct);

    public Task<PlayerLegion?> GetActiveAsync(
        Guid playerId, CancellationToken ct = default)
        => _db.PlayerLegions
            .FirstOrDefaultAsync(
                l => l.PlayerId == playerId && l.IsActive && !l.IsDeleted, ct);

    public async Task<PlayerLegion> UpsertAsync(
        Guid playerId, string legionDefinitionId, CancellationToken ct = default)
    {
        var existing = await FindAsync(playerId, legionDefinitionId, ct);
        if (existing is null)
        {
            var newLegion = PlayerLegion.Create(playerId, legionDefinitionId);
            _db.PlayerLegions.Add(newLegion);
            await _db.SaveChangesAsync(ct);
            return newLegion;
        }

        if (existing.IsDeleted)
        {
            existing.Restore();
            _db.PlayerLegions.Update(existing);
            await _db.SaveChangesAsync(ct);
        }

        return existing;
    }

    public async Task UpdateAsync(PlayerLegion legion, CancellationToken ct = default)
    {
        _db.PlayerLegions.Update(legion);
        await _db.SaveChangesAsync(ct);
    }
}
