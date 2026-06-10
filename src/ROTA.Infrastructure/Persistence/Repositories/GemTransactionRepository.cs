using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class GemTransactionRepository : IGemTransactionRepository
{
    // XOR salt for the per-player gem advisory-lock key, so gem locks live in their own key space
    // and can't collide with the raid-id advisory locks derived the same way elsewhere.
    private const long GemLockSalt = 0x47454D53; // "GEMS"

    private readonly RotaDbContext _db;

    public GemTransactionRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default)
        => await _db.GemTransactions
            .Where(t => t.PlayerId == playerId)
            .SumAsync(t => t.Amount, ct);

    public async Task<bool> ReferenceExistsAsync(
        Guid playerId, GemTransactionType type, string referenceId,
        CancellationToken ct = default)
        => await _db.GemTransactions
            .AnyAsync(t => t.PlayerId == playerId
                        && t.TransactionType == type
                        && t.ReferenceId == referenceId, ct);

    public async Task CreateAsync(GemTransaction transaction, CancellationToken ct = default)
    {
        _db.GemTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
    }

    // Audit fix — duplicate-grant safety. The unique partial index (player, type, reference) is the
    // hard backstop; this converts a concurrent duplicate's 23505 into a clean "already granted"
    // instead of an unhandled 500. Mirrors the unique-violation-idempotent pattern of the other ledgers.
    public async Task<bool> TryCreateAsync(GemTransaction transaction, CancellationToken ct = default)
    {
        _db.GemTransactions.Add(transaction);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            _db.Entry(transaction).State = EntityState.Detached;
            return false;
        }
    }

    // Audit fix — atomic spend. The old GemService flow (read SUM balance → insert −amount) raced:
    // two concurrent spends both saw the same balance, both passed, and the balance went negative.
    // Here the idempotency check, balance check, and debit insert all run under a per-player
    // advisory lock inside one transaction, so spends for a player serialize and each check sees
    // every previously committed spend. Participates in an ambient transaction when one is open
    // (the lock then rides that transaction); otherwise it owns its own.
    public async Task<GemSpendOutcome> TrySpendAsync(
        Guid playerId, int amount, GemTransactionType type, string? referenceId,
        CancellationToken ct = default)
    {
        bool ownTx = _db.Database.CurrentTransaction is null;
        IDbContextTransaction tx = ownTx
            ? await _db.Database.BeginTransactionAsync(ct)
            : _db.Database.CurrentTransaction!;
        try
        {
            var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
            var ntx  = (NpgsqlTransaction)tx.GetDbTransaction();

            var lockId = BitConverter.ToInt64(playerId.ToByteArray(), 0) ^ GemLockSalt;
            await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@k)", conn, ntx))
            {
                lockCmd.Parameters.AddWithValue("k", NpgsqlDbType.Bigint, lockId);
                await lockCmd.ExecuteNonQueryAsync(ct);
            }

            // Idempotency under the lock: a committed row for this reference means the original
            // charge succeeded — the caller proceeds with its (idempotent) grant step.
            if (referenceId is not null)
            {
                const string existsSql = """
                    SELECT EXISTS (
                        SELECT 1 FROM gem_transactions
                        WHERE player_id = @p AND transaction_type = @t AND reference_id = @r
                    )
                    """;
                await using var existsCmd = new NpgsqlCommand(existsSql, conn, ntx);
                existsCmd.Parameters.AddWithValue("p", NpgsqlDbType.Uuid, playerId);
                existsCmd.Parameters.AddWithValue("t", NpgsqlDbType.Integer, (int)type);
                existsCmd.Parameters.AddWithValue("r", NpgsqlDbType.Text, referenceId);
                if ((bool)(await existsCmd.ExecuteScalarAsync(ct))!)
                {
                    if (ownTx) await tx.CommitAsync(ct);
                    return GemSpendOutcome.AlreadyProcessed;
                }
            }

            // Conditional debit: the SUM-guard re-checks the balance in the same statement. With the
            // advisory lock serializing per-player spends, the guard always sees committed truth.
            const string insertSql = """
                INSERT INTO gem_transactions (player_id, amount, transaction_type, reference_id)
                SELECT @p, @neg, @t, @r
                WHERE (SELECT COALESCE(SUM(amount), 0) FROM gem_transactions WHERE player_id = @p) >= @cost
                """;
            await using var insertCmd = new NpgsqlCommand(insertSql, conn, ntx);
            insertCmd.Parameters.AddWithValue("p",    NpgsqlDbType.Uuid,    playerId);
            insertCmd.Parameters.AddWithValue("neg",  NpgsqlDbType.Integer, -amount);
            insertCmd.Parameters.AddWithValue("t",    NpgsqlDbType.Integer, (int)type);
            insertCmd.Parameters.AddWithValue("r",    NpgsqlDbType.Text,    (object?)referenceId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("cost", NpgsqlDbType.Integer, amount);

            var rows = await insertCmd.ExecuteNonQueryAsync(ct);
            if (ownTx) await tx.CommitAsync(ct);
            return rows == 1 ? GemSpendOutcome.Charged : GemSpendOutcome.InsufficientBalance;
        }
        finally
        {
            if (ownTx) await tx.DisposeAsync();
        }
    }
}
