using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — append-only Token + Pitchfork ledger access. Balance per currency =
// SUM(amount) WHERE currency = X. Same tri-state spend discipline as IStrikeRepository.
public interface IGauntletCurrencyRepository
{
    Task<long> GetBalanceAsync(Guid playerId, GauntletCurrency currency, CancellationToken ct = default);

    Task CreateAsync(GauntletCurrencyTransaction transaction, CancellationToken ct = default);

    Task<bool> ReferenceExistsAsync(
        Guid playerId, GauntletCurrency currency, GauntletCurrencyTransactionType type,
        string referenceId, CancellationToken ct = default);

    /// <summary>
    /// Atomically debits <paramref name="amount"/> of <paramref name="currency"/> with idempotency on
    /// <paramref name="referenceId"/>. Tri-state result identical in spirit to
    /// <see cref="IStrikeRepository.SpendAsync"/>.
    /// </summary>
    Task<GauntletCurrencySpendOutcome> SpendAsync(
        Guid playerId, GauntletCurrency currency, int amount, string referenceId,
        CancellationToken ct = default);
}
