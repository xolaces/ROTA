namespace ROTA.Application.Interfaces;

/// <summary>
/// Serializes a single player's mutating critical section under a Postgres advisory lock held for a
/// transaction, closing read-then-write (TOCTOU) races on per-player rows that have no concurrency
/// token of their own — stat allocation, item use, quest-progress depletion, gauntlet-shop spend.
/// Exploit audit 2026-06-14 (findings D/E/I/H).
///
/// Re-entrant: if an ambient transaction is already open (e.g. nested inside a raid hit's advisory-lock
/// transaction), the action runs directly on it rather than opening a second transaction.
/// </summary>
public interface IPlayerMutationLock
{
    /// <summary>Run <paramref name="action"/> with this player's advisory lock held; commit on success.</summary>
    Task<T> RunAsync<T>(Guid playerId, Func<Task<T>> action, CancellationToken ct = default);
}
