using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;

namespace ROTA.Infrastructure.Persistence.Repositories;

/// <summary>
/// Per-player serialization via <c>pg_advisory_xact_lock</c> held for a transaction — the same
/// advisory-lock discipline as ActiveRaidRepository.AtomicWithAdvisoryLockAsync / GemTransactionRepository.
/// Exploit audit 2026-06-14: closes the TOCTOU races on player_stats / player_inventory_items /
/// player_quest_progress / the gauntlet_currency spend path, which carry no per-row concurrency token.
/// </summary>
public sealed class PlayerMutationLock : IPlayerMutationLock
{
    private readonly RotaDbContext _db;

    public PlayerMutationLock(RotaDbContext db) => _db = db;

    public async Task<T> RunAsync<T>(Guid playerId, Func<Task<T>> action, CancellationToken ct = default)
    {
        // Re-entrant: an ambient transaction (e.g. a raid hit's advisory-lock tx) already serializes
        // this player's writes — run directly rather than nest a second transaction.
        if (_db.Database.CurrentTransaction is not null)
            return await action();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Lock key derived from the player id (same scheme as the raid/admin advisory locks). The lock
        // is held until this transaction ends, so a player's concurrent mutating requests run one at a
        // time and each sees the previous one's committed state.
        var lockId = BitConverter.ToInt64(playerId.ToByteArray(), 0);
        var conn   = (NpgsqlConnection)_db.Database.GetDbConnection();
        var ntx    = (NpgsqlTransaction)tx.GetDbTransaction();
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@k)", conn, ntx))
        {
            lockCmd.Parameters.AddWithValue("k", NpgsqlDbType.Bigint, lockId);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        // Discard anything tracked before the lock so reads inside the action see committed truth.
        _db.ChangeTracker.Clear();

        var result = await action();

        // Flush anything the action left pending, then commit atomically with the lock release.
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
