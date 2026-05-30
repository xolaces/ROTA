using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Persistence operations for beta keys.
/// </summary>
public interface IBetaKeyRepository
{
    /// <summary>Persists a newly generated beta key.</summary>
    Task<BetaKey> CreateAsync(BetaKey key, CancellationToken ct = default);

    /// <summary>
    /// Looks up a key by its string value.
    /// Returns null if the key does not exist or is soft-deleted.
    /// </summary>
    Task<BetaKey?> GetByKeyAsync(string key, CancellationToken ct = default);

    /// <summary>Returns the most-recently created keys (up to <paramref name="take"/>).</summary>
    Task<IReadOnlyList<BetaKey>> ListAsync(int take, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims the key for <paramref name="playerId"/> using a conditional UPDATE.
    /// The UPDATE succeeds only when <c>is_redeemed = false AND is_deleted = false</c>.
    /// Returns <c>true</c> if exactly one row was updated (key claimed successfully),
    /// <c>false</c> if the key was already redeemed, not found, or deleted.
    /// This is the race-condition guard for concurrent registration attempts.
    /// </summary>
    Task<bool> TryRedeemAsync(string key, Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Executes <paramref name="work"/> inside a database transaction, committing on success
    /// and rolling back on any exception. Used by <c>AuthService</c> to make key-claim +
    /// player-creation atomic: if player creation throws, the key is freed.
    /// </summary>
    Task<T> WithTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}
