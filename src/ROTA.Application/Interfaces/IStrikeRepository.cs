using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — append-only Strike ledger access. Balance = SUM(amount). Mirrors
// IGemTransactionRepository + the GemService idempotency discipline (the tri-state SpendAsync).
public interface IStrikeRepository
{
    /// <summary>Current Strike balance = SUM(amount) over all of the player's rows.</summary>
    Task<long> GetBalanceAsync(Guid playerId, CancellationToken ct = default);

    Task CreateAsync(StrikeTransaction transaction, CancellationToken ct = default);

    Task<bool> ReferenceExistsAsync(
        Guid playerId, StrikeTransactionType type, string referenceId, CancellationToken ct = default);

    /// <summary>
    /// Atomically debits <paramref name="amount"/> Strikes with idempotency on
    /// <paramref name="referenceId"/>. Returns <see cref="StrikeSpendOutcome.AlreadyCharged"/> if the
    /// reference already exists (idempotent replay), <see cref="StrikeSpendOutcome.Insufficient"/> if
    /// balance &lt; amount, otherwise inserts the −debit row and returns
    /// <see cref="StrikeSpendOutcome.Charged"/>.
    /// </summary>
    Task<StrikeSpendOutcome> SpendAsync(
        Guid playerId, int amount, string referenceId, CancellationToken ct = default);
}
