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

// System 21 Slice 3a — append-only guild sigil-economy ledgers (per-player currency + per-guild pool).
// Balances are SUM(amount); multi-row writes commit in a single SaveChanges so a buy (ticket debit +
// sigil credit) and a donation (personal debit + pool credit) are atomic. Duplicate references are
// caught via the unique partial indexes and reported as "not persisted" (idempotency backstop) —
// identical discipline to GauntletCurrencyRepository.
public sealed class GuildEconomyRepository : IGuildEconomyRepository
{
    private readonly RotaDbContext _db;

    public GuildEconomyRepository(RotaDbContext db) => _db = db;

    public async Task<long> GetPlayerBalanceAsync(
        Guid playerId, GuildCurrency currency, CancellationToken ct = default)
        => await _db.GuildCurrencyTransactions
            .Where(t => t.PlayerId == playerId && t.Currency == currency)
            .SumAsync(t => (long)t.Amount, ct);

    public async Task<long> GetPoolBalanceAsync(Guid guildId, CancellationToken ct = default)
        => await _db.GuildSigilPoolTransactions
            .Where(t => t.GuildId == guildId)
            .SumAsync(t => (long)t.Amount, ct);

    public async Task<int> CountByReferencePrefixAsync(
        Guid playerId, GuildCurrency currency, GuildCurrencyTransactionType type,
        string referencePrefix, CancellationToken ct = default)
        => await _db.GuildCurrencyTransactions
            .CountAsync(t => t.PlayerId == playerId
                          && t.Currency == currency
                          && t.TransactionType == type
                          && t.ReferenceId != null
                          && t.ReferenceId.StartsWith(referencePrefix), ct);

    public Task<bool> ReferenceExistsAsync(
        Guid playerId, GuildCurrency currency, GuildCurrencyTransactionType type,
        string referenceId, CancellationToken ct = default)
        => _db.GuildCurrencyTransactions.AnyAsync(
            t => t.PlayerId == playerId
              && t.Currency == currency
              && t.TransactionType == type
              && t.ReferenceId == referenceId, ct);

    public async Task<bool> AddPlayerTransactionsAsync(
        IReadOnlyList<GuildCurrencyTransaction> transactions, CancellationToken ct = default)
    {
        if (transactions.Count == 0) return true;
        try
        {
            _db.GuildCurrencyTransactions.AddRange(transactions);
            await _db.SaveChangesAsync(ct); // single transaction — all rows or none
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> AddDonationAsync(
        GuildCurrencyTransaction playerDebit, GuildSigilPoolTransaction poolCredit,
        CancellationToken ct = default)
    {
        try
        {
            _db.GuildCurrencyTransactions.Add(playerDebit);
            _db.GuildSigilPoolTransactions.Add(poolCredit);
            await _db.SaveChangesAsync(ct); // single transaction — both rows or neither
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<GuildPoolSpendOutcome> TrySpendPoolAsync(
        Guid guildId, int amount, string referenceId, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Participate in the ambient transaction (e.g. a summon tx) when one is open; null standalone.
        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        // 1. Idempotency-first: a RaidSummon row for this reference means the original debit committed.
        const string existsSql = """
            SELECT EXISTS (
                SELECT 1 FROM guild_sigil_pool_transactions
                WHERE guild_id         = @guildId
                  AND transaction_type = @type
                  AND reference_id     = @referenceId
            )
            """;
        await using (var existsCmd = new NpgsqlCommand(existsSql, conn, dbTx))
        {
            existsCmd.Parameters.AddWithValue("guildId",     NpgsqlDbType.Uuid,    guildId);
            existsCmd.Parameters.AddWithValue("type",        NpgsqlDbType.Integer, (int)GuildSigilPoolTransactionType.RaidSummon);
            existsCmd.Parameters.AddWithValue("referenceId", NpgsqlDbType.Text,    referenceId);
            var exists = (bool)(await existsCmd.ExecuteScalarAsync(ct))!;
            if (exists)
                return GuildPoolSpendOutcome.AlreadyCharged;
        }

        // 2. Conditional INSERT of the −debit row guarded by the pool-balance check in the same
        //    statement — atomic at the row level (rows 1 ⇒ Charged, 0 ⇒ Insufficient). The unique
        //    partial index is the hard backstop against a concurrent duplicate reference. Raw SQL, so
        //    no ChangeTracker interaction.
        const string insertSql = """
            INSERT INTO guild_sigil_pool_transactions (id, guild_id, amount, transaction_type, reference_id, created_at)
            SELECT gen_random_uuid(), @guildId, @amount, @type, @referenceId, NOW()
            WHERE (SELECT COALESCE(SUM(amount), 0) FROM guild_sigil_pool_transactions WHERE guild_id = @guildId) >= @cost
            """;
        await using var insertCmd = new NpgsqlCommand(insertSql, conn, dbTx);
        insertCmd.Parameters.AddWithValue("guildId",     NpgsqlDbType.Uuid,    guildId);
        insertCmd.Parameters.AddWithValue("amount",      NpgsqlDbType.Integer, -amount);
        insertCmd.Parameters.AddWithValue("type",        NpgsqlDbType.Integer, (int)GuildSigilPoolTransactionType.RaidSummon);
        insertCmd.Parameters.AddWithValue("referenceId", NpgsqlDbType.Text,    referenceId);
        insertCmd.Parameters.AddWithValue("cost",        NpgsqlDbType.Integer, amount);

        try
        {
            int rows = await insertCmd.ExecuteNonQueryAsync(ct);
            return rows == 1 ? GuildPoolSpendOutcome.Charged : GuildPoolSpendOutcome.Insufficient;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Concurrent caller inserted the same reference — the original debit committed. Idempotent.
            return GuildPoolSpendOutcome.AlreadyCharged;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
