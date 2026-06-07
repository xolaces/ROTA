using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
