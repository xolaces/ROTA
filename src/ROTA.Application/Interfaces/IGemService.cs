using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IGemService
{
    /// <summary>Returns the player's current gem balance computed from the ledger.</summary>
    Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Credits gems to the player.
    /// Returns false if referenceId is provided and already used (idempotency rejection).
    /// </summary>
    Task<bool> GrantGemsAsync(Guid playerId, int amount, GemTransactionType type, string? referenceId, CancellationToken ct = default);

    /// <summary>
    /// Debits gems from the player.
    /// Returns false if balance is insufficient or referenceId is already used.
    /// </summary>
    Task<bool> SpendGemsAsync(Guid playerId, int amount, GemTransactionType type, string? referenceId, CancellationToken ct = default);

    /// <summary>
    /// Grants 5 daily gems. Enforced once per UTC day via referenceId = "daily:{yyyy-MM-dd}".
    /// Returns false if already claimed today.
    /// </summary>
    Task<bool> DailyRefillAsync(Guid playerId, CancellationToken ct = default);
}
