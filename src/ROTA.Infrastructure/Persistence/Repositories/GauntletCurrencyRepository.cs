using Microsoft.EntityFrameworkCore;
using Npgsql;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA (System 16 Slice 2) — append-only Token + Pitchfork ledger. Balance is per-currency; the
// idempotency index folds the currency in so the same referenceId may credit Token AND Pitchfork.
public sealed class GauntletCurrencyRepository : IGauntletCurrencyRepository
{
    private readonly RotaDbContext _db;

    public GauntletCurrencyRepository(RotaDbContext db) => _db = db;

    public async Task<long> GetBalanceAsync(
        Guid playerId, GauntletCurrency currency, CancellationToken ct = default)
        => await _db.GauntletCurrencyTransactions
            .Where(t => t.PlayerId == playerId && t.Currency == currency)
            .SumAsync(t => (long)t.Amount, ct);

    public async Task CreateAsync(
        GauntletCurrencyTransaction transaction, CancellationToken ct = default)
    {
        _db.GauntletCurrencyTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> ReferenceExistsAsync(
        Guid playerId, GauntletCurrency currency, GauntletCurrencyTransactionType type,
        string referenceId, CancellationToken ct = default)
        => _db.GauntletCurrencyTransactions.AnyAsync(
            t => t.PlayerId == playerId
              && t.Currency == currency
              && t.TransactionType == type
              && t.ReferenceId == referenceId, ct);

    public async Task<GauntletCurrencySpendOutcome> SpendAsync(
        Guid playerId, GauntletCurrency currency, int amount, string referenceId,
        CancellationToken ct = default)
    {
        // Spends are always ShopPurchase debits (Slice 6 is the only spender). Idempotency-first,
        // then balance, then guarded insert — identical discipline to StrikeRepository.SpendAsync.
        if (await ReferenceExistsAsync(
                playerId, currency, GauntletCurrencyTransactionType.ShopPurchase, referenceId, ct))
            return GauntletCurrencySpendOutcome.AlreadyCharged;

        var balance = await GetBalanceAsync(playerId, currency, ct);
        if (balance < amount)
            return GauntletCurrencySpendOutcome.Insufficient;

        try
        {
            _db.GauntletCurrencyTransactions.Add(GauntletCurrencyTransaction.Create(
                playerId, currency, -amount, GauntletCurrencyTransactionType.ShopPurchase, referenceId));
            await _db.SaveChangesAsync(ct);
            return GauntletCurrencySpendOutcome.Charged;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return GauntletCurrencySpendOutcome.AlreadyCharged;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
