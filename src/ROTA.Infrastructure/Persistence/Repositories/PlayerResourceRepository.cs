using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerResourceRepository : IPlayerResourceRepository
{
    private readonly RotaDbContext _db;

    public PlayerResourceRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<PlayerResource?> GetAsync(Guid playerId, ResourceType type, CancellationToken ct = default)
        => await _db.PlayerResources
            .FirstOrDefaultAsync(r => r.PlayerId == playerId && r.ResourceType == type, ct);

    public async Task<bool> AtomicUpdateAsync(
        Guid playerId,
        ResourceType type,
        Func<PlayerResource, bool> updateFn,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // FOR UPDATE acquires a row-level lock for the duration of the transaction.
        var resource = await _db.PlayerResources
            .FromSqlInterpolated(
                $"SELECT * FROM player_resources WHERE player_id = {playerId} AND resource_type = {type.ToString()} FOR UPDATE")
            .FirstOrDefaultAsync(ct);

        if (resource is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        if (!updateFn(resource))
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
