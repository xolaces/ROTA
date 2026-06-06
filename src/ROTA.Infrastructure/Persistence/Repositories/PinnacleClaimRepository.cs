using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PinnacleClaimRepository : IPinnacleClaimRepository
{
    private readonly RotaDbContext _db;

    public PinnacleClaimRepository(RotaDbContext db) => _db = db;

    public async Task<bool> TryClaimAsync(int pinnacleLevel, Guid playerId, CancellationToken ct = default)
    {
        // Fast path: already claimed (avoids the exception in the common case).
        if (await _db.PinnacleFirstClaims.AnyAsync(c => c.PinnacleLevel == pinnacleLevel, ct))
            return false;

        try
        {
            _db.PinnacleFirstClaims.Add(PinnacleFirstClaim.Create(pinnacleLevel, playerId));
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost the race — another player's insert won the unique index. Detach the failed insert so
            // a later SaveChanges on this (scoped) context doesn't retry it.
            foreach (var entry in _db.ChangeTracker
                         .Entries<PinnacleFirstClaim>()
                         .Where(e => e.State == EntityState.Added)
                         .ToList())
                entry.State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<PinnacleFirstClaim>> ListAsync(CancellationToken ct = default)
        => await _db.PinnacleFirstClaims
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.PinnacleLevel)
            .ToListAsync(ct);
}
