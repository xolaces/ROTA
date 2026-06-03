using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

// BETA — aggregate-increment repository for the leaderboard_entry table.
// All writes are race-safe PostgreSQL upserts (ON CONFLICT DO UPDATE) —
// concurrent increments on the same (player, board, period_key) never lose updates.
public interface ILeaderboardEntryRepository
{
    /// <summary>
    /// Race-safe upsert+add for Sum boards (EnergySpent, DamageDealt).
    /// On first call: inserts a row with Value = <paramref name="delta"/>.
    /// On subsequent calls: atomically adds <paramref name="delta"/> to the existing Value.
    /// Uses PostgreSQL <c>INSERT … ON CONFLICT (player_id, board, period_key) DO UPDATE SET
    /// value = leaderboard_entry.value + EXCLUDED.value</c> so concurrent increments
    /// are serialised at the database level with no lost updates.
    /// Both the insert and the update write <paramref name="at"/> to <c>last_progress_at</c>
    /// and <c>NOW()</c> to <c>updated_at</c>.
    /// </summary>
    Task IncrementAsync(
        Guid playerId,
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string periodKey,
        long delta,
        DateTimeOffset at,
        CancellationToken ct = default);

    /// <summary>
    /// Race-safe upsert+max for Max boards (LargestHit).
    /// On first call: inserts a row with Value = <paramref name="candidate"/>.
    /// On subsequent calls: updates Value only when <paramref name="candidate"/> exceeds the
    /// stored value (<c>GREATEST(leaderboard_entry.value, EXCLUDED.value)</c>).
    /// <c>last_progress_at</c> advances only when the value actually rises (conditional CASE).
    /// </summary>
    Task MaxUpdateAsync(
        Guid playerId,
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string periodKey,
        long candidate,
        DateTimeOffset at,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a page of entries for a board+period_key, ordered by Value DESC then
    /// LastProgressAt ASC (earliest-to-reach tiebreak). Excludes soft-deleted rows.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    Task<IReadOnlyList<LeaderboardEntry>> GetPageAsync(
        LeaderboardBoard board,
        string periodKey,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a single entry for the given player on a board+period_key via the unique index,
    /// or <c>null</c> if no entry exists.
    /// </summary>
    Task<LeaderboardEntry?> GetPlayerEntryAsync(
        Guid playerId,
        LeaderboardBoard board,
        string periodKey,
        CancellationToken ct = default);
}
