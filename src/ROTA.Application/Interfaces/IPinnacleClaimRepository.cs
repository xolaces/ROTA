using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>Persistence for pinnacle first-claims (T33).</summary>
public interface IPinnacleClaimRepository
{
    /// <summary>
    /// Atomically claims <paramref name="pinnacleLevel"/> for <paramref name="playerId"/>. Returns true
    /// if this player was the first (a row was inserted); false if the level was already claimed
    /// (idempotent — relies on the unique index, like the gem ledger's reference idempotency).
    /// </summary>
    Task<bool> TryClaimAsync(int pinnacleLevel, Guid playerId, CancellationToken ct = default);

    /// <summary>All claims, ordered by pinnacle level (for the ops dashboard).</summary>
    Task<IReadOnlyList<PinnacleFirstClaim>> ListAsync(CancellationToken ct = default);
}
