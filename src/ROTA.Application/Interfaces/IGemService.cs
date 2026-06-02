using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IGemService
{
    Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default);

    Task<bool> GrantGemsAsync(Guid playerId, int amount, GemTransactionType type, string? referenceId, CancellationToken ct = default);

    /// <summary>
    /// Attempts to deduct <paramref name="amount"/> gems from the player's ledger.
    /// </summary>
    /// <returns>
    /// <see cref="GemSpendOutcome.Charged"/> if the ledger row was written successfully;
    /// <see cref="GemSpendOutcome.AlreadyProcessed"/> if <paramref name="referenceId"/> already
    /// exists — the original charge committed (idempotent replay, treat as success);
    /// <see cref="GemSpendOutcome.InsufficientBalance"/> if the balance was too low.
    /// </returns>
    Task<GemSpendOutcome> SpendGemsAsync(Guid playerId, int amount, GemTransactionType type, string? referenceId, CancellationToken ct = default);

    Task<bool> DailyRefillAsync(Guid playerId, CancellationToken ct = default);
}
