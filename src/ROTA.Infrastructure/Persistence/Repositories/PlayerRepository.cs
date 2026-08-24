using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerRepository : IPlayerRepository
{
    private readonly RotaDbContext _db;

    public PlayerRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<Player?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Players
            .Where(p => p.Id == id && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<Player?> FindByEmailAsync(string email, CancellationToken ct = default)
        => await _db.Players
            .Where(p => p.Email == email.ToLowerInvariant() && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => await _db.Players
            .AnyAsync(p => p.Email == email.ToLowerInvariant() && !p.IsDeleted, ct);

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => await _db.Players
            .AnyAsync(p => p.Username == username && !p.IsDeleted, ct);

    public async Task<Player> CreateAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Add(player);
        await _db.SaveChangesAsync(ct);
        return player;
    }

    public async Task<Player?> FindByIdWithResourcesAsync(Guid id, CancellationToken ct = default)
        => await _db.Players
            .Include(p => p.Resources)
            .Where(p => p.Id == id && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<Player?> FindByIdWithStatsAsync(Guid id, CancellationToken ct = default)
        => await _db.Players
            .Include(p => p.Stats)
            .Where(p => p.Id == id && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task UpdateAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Update(player);
        await _db.SaveChangesAsync(ct);
    }

    // T59 — reward-write chokepoint. The players row carries xmin as a concurrency token, so a save
    // racing another request's commit throws DbUpdateConcurrencyException; we reload the row's fresh
    // values (same tracked instance — the DbContext identity map guarantees later reads in the request
    // see the committed state) and re-apply the mutation. A player can only race themselves (quest vs
    // raid vs gauntlet hits), so contention is shallow — the generous cap exists purely so a burst of
    // simultaneous hits never surfaces a 500 where a retry would have converged in microseconds.
    public async Task<TResult> MutateWithRetryAsync<TResult>(Guid playerId, Func<Player, TResult> mutate, CancellationToken ct = default)
    {
        const int maxAttempts = 10;
        for (int attempt = 1; ; attempt++)
        {
            var player = await FindByIdAsync(playerId, ct)
                ?? throw new InvalidOperationException($"Player {playerId} not found for reward mutation.");
            var result = mutate(player);
            try
            {
                await _db.SaveChangesAsync(ct);
                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                // Discard the conflicted values and pull the committed row; the loop re-applies.
                await _db.Entry(player).ReloadAsync(ct);
            }
        }
    }

    // D-008/D-013 gold sink. Conditional debit: the `gold >= @amount` guard lives in the SAME statement
    // as the subtraction, so there is no window between checking affordability and spending — the shape
    // that let concurrent gem buys drive a balance negative before the ledger got its advisory lock.
    // Raw SQL rather than EF because a tracked read-modify-save reintroduces exactly that window, and
    // because the players row carries an xmin token (T59) an entity write would also conflict with a
    // concurrent reward grant. RETURNING hands back the committed balance without a second read.
    // Ambient-transaction aware: inside a mutation-lock transaction this enlists and commits (or rolls
    // back) together with whatever the gold paid for.
    public async Task<long?> TrySpendGoldAsync(Guid playerId, long amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Gold spend must be positive.");

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        var ntx = (NpgsqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();

        const string sql = """
            UPDATE players
            SET gold = gold - @amount, updated_at = now()
            WHERE id = @p AND NOT is_deleted AND gold >= @amount
            RETURNING gold
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, ntx);
        cmd.Parameters.AddWithValue("p", NpgsqlDbType.Uuid, playerId);
        cmd.Parameters.AddWithValue("amount", NpgsqlDbType.Bigint, amount);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull) return null;   // unaffordable / missing → nothing written

        // The row was changed underneath EF; drop any stale tracked copy so later reads in this request
        // see the committed balance rather than the pre-debit one.
        var tracked = _db.ChangeTracker.Entries<Player>()
            .FirstOrDefault(e => e.Entity.Id == playerId);
        if (tracked is not null) await tracked.ReloadAsync(ct);

        return (long)result;
    }

    public async Task UpdateStatsAsync(Domain.Entities.PlayerStats stats, CancellationToken ct = default)
    {
        _db.PlayerStats.Update(stats);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Player?> FindByUsernameAsync(string username, CancellationToken ct = default)
        => await _db.Players
            .Where(p => p.Username == username && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<int> CountByRoleAsync(PlayerRoles role, CancellationToken ct = default)
        => await _db.Players
            .CountAsync(p => !p.IsDeleted && (p.Roles & role) == role, ct);

    // Fixed advisory-lock key for ALL admin-role mutations. A single conditional UPDATE with a COUNT
    // subquery would NOT be safe here — concurrent demotions target DIFFERENT rows, so row locks don't
    // serialize them and both could read count==2 under READ COMMITTED. The advisory lock forces them
    // to run one at a time, so the count the winner reads already reflects any prior commit.
    private const long AdminRoleLockKey = unchecked((long)0xAD17_0E55_0A001L);

    public async Task<bool> TryDemoteAdminAsync(Guid targetId, CancellationToken ct = default)
    {
        await using IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct);

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        var ntx  = (NpgsqlTransaction)tx.GetDbTransaction();
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@k)", conn, ntx))
        {
            lockCmd.Parameters.AddWithValue("k", NpgsqlDbType.Bigint, AdminRoleLockKey);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        // Re-read under the lock so the count reflects every committed change.
        _db.ChangeTracker.Clear();

        var adminCount = await _db.Players
            .CountAsync(p => !p.IsDeleted && (p.Roles & PlayerRoles.Admin) == PlayerRoles.Admin, ct);
        var target = await _db.Players
            .FirstOrDefaultAsync(p => p.Id == targetId && !p.IsDeleted, ct);

        if (target is null || !target.HasRole(PlayerRoles.Admin) || adminCount <= 1)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        target.RevokeRole(PlayerRoles.Admin);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
