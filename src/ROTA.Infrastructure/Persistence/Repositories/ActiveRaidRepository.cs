using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class ActiveRaidRepository : IActiveRaidRepository
{
    private readonly RotaDbContext _db;

    public ActiveRaidRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<ActiveRaid?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ActiveRaids
            .Where(r => r.Id == id && !r.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ActiveRaid>> GetAllActiveAsync(CancellationToken ct = default)
        => await _db.ActiveRaids
            .Include(r => r.SummonedByPlayer)
            .Where(r => !r.IsDefeated && !r.IsDeleted && r.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

    public async Task<ActiveRaid> CreateAsync(ActiveRaid raid, CancellationToken ct = default)
    {
        _db.ActiveRaids.Add(raid);
        await _db.SaveChangesAsync(ct);
        return raid;
    }

    public async Task UpdateAsync(ActiveRaid raid, CancellationToken ct = default)
    {
        _db.ActiveRaids.Update(raid);
        await _db.SaveChangesAsync(ct);
    }

    // AtomicApplyHitAsync — serialisation + reward-atomicity wrapper.
    //
    // Serialisation: pg_advisory_xact_lock acquires a PostgreSQL advisory lock on a stable
    // int64 derived from raidId.  Only one transaction per raidId executes the critical
    // section at a time; all others block until the lock holder commits or rolls back, at
    // which point they see the committed state (READ COMMITTED).  The lock is released
    // automatically when the transaction ends — no explicit release needed.
    //
    // Atomicity: the EF Core transaction wraps all repositories sharing this DbContext
    // (participants, players, resources, gems, stats, inventory), so kill rewards are committed
    // exactly once — and only after the kill is confirmed.
    //
    // Stamina spend runs INSIDE this transaction (v0.2.5): EnergyService → PlayerResourceRepository
    // .AtomicUpdateAsync detects the ambient transaction and participates in it, so a rolled-back
    // hit also rolls back the spend. No separate refund path is needed.
    public async Task<bool> AtomicApplyHitAsync(
        Guid raidId,
        Func<ActiveRaid, Task<bool>> mutate,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Advisory lock — issued on the same Npgsql connection/transaction that the EF Core
        // DbContext is using, so PostgreSQL holds the lock until this transaction ends.
        // pg_advisory_xact_lock blocks any other session that requests the same key, which
        // serialises concurrent hits on this raid without any external lock service.
        var lockId = BitConverter.ToInt64(raidId.ToByteArray(), 0);
        var npgsqlConn = (NpgsqlConnection)_db.Database.GetDbConnection();
        var npgsqlTx   = (NpgsqlTransaction)tx.GetDbTransaction();
        await using var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@lockId)", npgsqlConn, npgsqlTx);
        lockCmd.Parameters.AddWithValue("lockId", NpgsqlDbType.Bigint, lockId);
        await lockCmd.ExecuteNonQueryAsync(ct);

        // EF Core identity resolution would return the stale entity already in the change
        // tracker (loaded by FindByIdAsync earlier in HitRaidAsync) instead of materialising
        // a fresh row from the database.  Clearing the tracker forces EF Core to issue a
        // real query and return whatever is committed — critical after the advisory lock
        // unblocks and the winner's changes are visible.
        _db.ChangeTracker.Clear();

        var raid = await _db.ActiveRaids
            .Where(r => r.Id == raidId && !r.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (raid is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        if (!await mutate(raid))
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        // SaveChanges here captures any entity changes not yet flushed by sub-operations
        // (e.g., the raid's UpdatedAt from TakeDamage/MarkDefeated if not saved earlier).
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
