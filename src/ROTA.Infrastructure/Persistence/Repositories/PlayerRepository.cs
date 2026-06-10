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
