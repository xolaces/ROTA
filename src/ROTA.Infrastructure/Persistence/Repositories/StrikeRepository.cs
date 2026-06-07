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

// BETA (System 16 Slice 2) — append-only Strike ledger. Mirrors GemTransactionRepository +
// GemService.SpendGemsAsync, with the atomic-spend discipline of BetaKeyRepository.
//
// BETA (System 16 Slice 4) — SpendAsync is now tx-SAFE: it uses raw parameterised Npgsql that
// participates in _db.Database.CurrentTransaction (mirrors GauntletEntryRepository.IncrementScoreAsync),
// so a strike debit commits/rolls back atomically with the RaidService advisory-lock hit. It NEVER
// calls ChangeTracker.Clear() (which would detach the tracked locked raid). The tri-state contract is
// preserved so the standalone Slice-2 strike tests (no ambient tx) still pass — when there is no
// ambient transaction, the connection is opened directly and the single conditional INSERT is itself
// atomic at the row level.
public sealed class StrikeRepository : IStrikeRepository
{
    private readonly RotaDbContext _db;

    public StrikeRepository(RotaDbContext db) => _db = db;

    public async Task<long> GetBalanceAsync(Guid playerId, CancellationToken ct = default)
        => await _db.StrikeTransactions
            .Where(t => t.PlayerId == playerId)
            .SumAsync(t => (long)t.Amount, ct);

    public async Task CreateAsync(StrikeTransaction transaction, CancellationToken ct = default)
    {
        _db.StrikeTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> ReferenceExistsAsync(
        Guid playerId, StrikeTransactionType type, string referenceId, CancellationToken ct = default)
        => _db.StrikeTransactions.AnyAsync(
            t => t.PlayerId == playerId
              && t.TransactionType == type
              && t.ReferenceId == referenceId, ct);

    public async Task<StrikeSpendOutcome> SpendAsync(
        Guid playerId, int amount, string referenceId, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        // Pick up the ambient transaction (e.g. the RaidService advisory-lock tx in Slice 4) so the
        // strike debit commits atomically with the hit. Null when called standalone (Slice-2 tests).
        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        // 1. Idempotency-first (mirrors GemService.SpendGemsAsync): if the HitSpend reference already
        //    exists the original debit committed — AlreadyCharged, never re-debit, never confuse with
        //    Insufficient.
        const string existsSql = """
            SELECT EXISTS (
                SELECT 1 FROM strike_transactions
                WHERE player_id        = @playerId
                  AND transaction_type = @type
                  AND reference_id     = @referenceId
            )
            """;
        await using (var existsCmd = new NpgsqlCommand(existsSql, conn, dbTx))
        {
            existsCmd.Parameters.AddWithValue("playerId",    NpgsqlDbType.Uuid,    playerId);
            existsCmd.Parameters.AddWithValue("type",        NpgsqlDbType.Integer, (int)StrikeTransactionType.HitSpend);
            existsCmd.Parameters.AddWithValue("referenceId", NpgsqlDbType.Text,    referenceId);
            var exists = (bool)(await existsCmd.ExecuteScalarAsync(ct))!;
            if (exists)
                return StrikeSpendOutcome.AlreadyCharged;
        }

        // 2. Conditional INSERT of the −debit row guarded by a balance check in the same statement.
        //    INSERT … SELECT … WHERE (SELECT COALESCE(SUM(amount),0) …) >= @cost is atomic at the row
        //    level: rows-affected 1 ⇒ Charged, 0 ⇒ Insufficient. The unique partial index on
        //    (player_id, transaction_type, reference_id) is the hard backstop against a concurrent
        //    duplicate reference — a unique violation is translated to AlreadyCharged (idempotent),
        //    never surfaced. No ChangeTracker interaction: this is raw SQL, so the tracked locked raid
        //    in the ambient tx is never detached.
        const string insertSql = """
            INSERT INTO strike_transactions (id, player_id, amount, transaction_type, reference_id, created_at)
            SELECT gen_random_uuid(), @playerId, @amount, @type, @referenceId, NOW()
            WHERE (SELECT COALESCE(SUM(amount), 0) FROM strike_transactions WHERE player_id = @playerId) >= @cost
            """;
        await using var insertCmd = new NpgsqlCommand(insertSql, conn, dbTx);
        insertCmd.Parameters.AddWithValue("playerId",    NpgsqlDbType.Uuid,    playerId);
        insertCmd.Parameters.AddWithValue("amount",      NpgsqlDbType.Integer, -amount);
        insertCmd.Parameters.AddWithValue("type",        NpgsqlDbType.Integer, (int)StrikeTransactionType.HitSpend);
        insertCmd.Parameters.AddWithValue("referenceId", NpgsqlDbType.Text,    referenceId);
        insertCmd.Parameters.AddWithValue("cost",        NpgsqlDbType.Integer, amount);

        try
        {
            int rows = await insertCmd.ExecuteNonQueryAsync(ct);
            return rows == 1 ? StrikeSpendOutcome.Charged : StrikeSpendOutcome.Insufficient;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // A concurrent caller inserted the same reference between our existence check and this
            // write — the original debit committed. Idempotent replay.
            return StrikeSpendOutcome.AlreadyCharged;
        }
    }

    private static async Task EnsureOpenAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
    }
}
