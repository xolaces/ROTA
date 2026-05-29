using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class BetaKeyRepository : IBetaKeyRepository
{
    private readonly RotaDbContext _db;

    public BetaKeyRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<BetaKey> CreateAsync(BetaKey key, CancellationToken ct = default)
    {
        _db.BetaKeys.Add(key);
        await _db.SaveChangesAsync(ct);
        return key;
    }

    public async Task<BetaKey?> GetByKeyAsync(string key, CancellationToken ct = default)
        => await _db.BetaKeys
            .Where(k => k.Key == key && !k.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<BetaKey>> ListAsync(int take, CancellationToken ct = default)
        => await _db.BetaKeys
            .Where(k => !k.IsDeleted)
            .OrderByDescending(k => k.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<bool> TryRedeemAsync(string key, Guid playerId, CancellationToken ct = default)
    {
        // Single atomic UPDATE — the WHERE clause is the race-condition guard.
        // Only one concurrent request can match is_redeemed=false; the winner gets rowsAffected=1.
        var rowsAffected = await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE beta_keys
               SET is_redeemed           = true,
                   redeemed_by_player_id = {0},
                   redeemed_at           = now(),
                   updated_at            = now()
             WHERE key         = {1}
               AND is_redeemed = false
               AND is_deleted  = false
            """,
            parameters: new object[] { playerId, key },
            cancellationToken: ct);

        return rowsAffected == 1;
    }

    public async Task<T> WithTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
