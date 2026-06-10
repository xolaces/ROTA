using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IGemTransactionRepository
{
    Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default);

    Task<bool> ReferenceExistsAsync(Guid playerId, GemTransactionType type, string referenceId, CancellationToken ct = default);

    Task CreateAsync(GemTransaction transaction, CancellationToken ct = default);

    /// <summary>
    /// Audit fix — atomic spend. Idempotency check, balance check, and the −amount insert run under a
    /// per-player advisory lock in one transaction, so concurrent spends serialize and can never drive
    /// the SUM-ledger balance negative (the old check-then-insert in GemService raced).
    /// Participates in an ambient transaction when one is open.
    /// </summary>
    Task<GemSpendOutcome> TrySpendAsync(Guid playerId, int amount, GemTransactionType type, string? referenceId, CancellationToken ct = default);

    /// <summary>
    /// Insert that treats a duplicate (player, type, reference) unique-index violation as "already
    /// granted" (returns false) instead of throwing — closes the concurrent-duplicate-grant 500.
    /// </summary>
    Task<bool> TryCreateAsync(GemTransaction transaction, CancellationToken ct = default);
}
