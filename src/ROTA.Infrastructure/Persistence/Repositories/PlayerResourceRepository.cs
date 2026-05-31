using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    // AtomicUpdateAsync participates in an ambient transaction when one is already open
    // (e.g. the advisory-lock transaction in ActiveRaidRepository.AtomicApplyHitAsync).
    // When no ambient transaction exists, it begins and commits its own — same behaviour as before.
    // This makes stamina spend atomic with the raid hit: a rolled-back hit also rolls back the spend.
    public async Task<bool> AtomicUpdateAsync(
        Guid playerId,
        ResourceType type,
        Func<PlayerResource, bool> updateFn,
        CancellationToken ct = default)
    {
        bool ownTx = _db.Database.CurrentTransaction is null;
        IDbContextTransaction? tx = ownTx ? await _db.Database.BeginTransactionAsync(ct) : null;

        try
        {
            var resource = await _db.PlayerResources
                .FromSqlInterpolated(
                    $"SELECT * FROM player_resources WHERE player_id = {playerId} AND resource_type = {type.ToString()} FOR UPDATE")
                .FirstOrDefaultAsync(ct);

            if (resource is null || !updateFn(resource))
            {
                if (ownTx && tx is not null) await tx.RollbackAsync(ct);
                return false;
            }

            await _db.SaveChangesAsync(ct);
            if (ownTx && tx is not null) await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            if (ownTx && tx is not null)
            {
                try { await tx.RollbackAsync(ct); } catch { /* swallow secondary failure */ }
            }
            throw;
        }
        finally
        {
            if (ownTx) tx?.Dispose();
        }
    }
}
