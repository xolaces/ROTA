using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2 + 3) — persistence for per-event player standings.
public interface IGauntletEntryRepository
{
    Task<GauntletEntry?> FindByEventAndPlayerAsync(
        Guid gauntletEventId, Guid playerId, CancellationToken ct = default);

    /// <summary>All non-deleted entries for an event (used by the snapshot/leaderboard in Slice 3).</summary>
    Task<IReadOnlyList<GauntletEntry>> GetForEventAsync(
        Guid gauntletEventId, CancellationToken ct = default);

    /// <summary>
    /// Inserts the entry if (event, player) has none, else returns the existing row unchanged.
    /// The unique index on (gauntlet_event_id, player_id) makes this race-safe; a conflicting
    /// insert is swallowed and the persisted row is re-read (so league is never re-evaluated).
    /// </summary>
    Task<GauntletEntry> UpsertAsync(GauntletEntry entry, CancellationToken ct = default);

    // ── Slice 3: scoring + ranked reads (raw SQL, mirrors LeaderboardEntryRepository) ───────────

    /// <summary>
    /// Atomically adds <paramref name="delta"/> to the (event, player) entry's score in a single
    /// race-safe UPDATE — never load-modify-save. <c>tie_break_at</c> advances to
    /// <paramref name="hitAt"/> ONLY when <paramref name="delta"/> &gt; 0 (zero/negative deltas
    /// must not move the tiebreak). Participates in an ambient transaction if one is present
    /// (e.g. the RaidService advisory-lock tx in Slice 4). No-op if the entry does not exist.
    /// </summary>
    Task IncrementScoreAsync(
        Guid eventId, Guid playerId, long delta, DateTimeOffset hitAt, CancellationToken ct = default);

    /// <summary>
    /// Snapshots per-league ranks for the event into <c>last_rank</c> via one UPDATE using
    /// <c>ROW_NUMBER() OVER (PARTITION BY league ORDER BY score DESC, tie_break_at ASC)</c>.
    /// Idempotent: re-running over an unchanged board yields identical ranks.
    /// </summary>
    Task RecomputeRanksAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Returns the top <paramref name="take"/> entries for a league in an event, ordered by the
    /// snapshot rank (<c>last_rank</c> ASC), joined to <c>players</c> for the display name.
    /// Entries with a null <c>last_rank</c> (not yet snapshotted) are excluded.
    /// </summary>
    Task<IReadOnlyList<GauntletLeaderboardRow>> GetLeaderboardPageAsync(
        Guid eventId, GauntletLeague league, int take, CancellationToken ct = default);

    /// <summary>
    /// Returns the caller's snapshot rank + current score for an event, or <c>null</c> when the
    /// caller has no (non-deleted) entry in that event. When <paramref name="league"/> is supplied,
    /// the entry must also be in that league (a player has one locked league per event, so a
    /// mismatch means they do not belong to that league's board → null).
    /// </summary>
    Task<GauntletRankScore?> GetRankAndScoreAsync(
        Guid eventId, Guid playerId, GauntletLeague? league = null, CancellationToken ct = default);

    /// <summary>Count of entries in a league+event that carry a non-null snapshot rank.</summary>
    Task<int> CountRankedAsync(
        Guid eventId, GauntletLeague league, CancellationToken ct = default);
}

/// <summary>One ranked leaderboard row hydrated with the player's display name.</summary>
public sealed class GauntletLeaderboardRow
{
    public int Rank { get; init; }
    public Guid PlayerId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public long Score { get; init; }
}

/// <summary>The caller's own snapshot rank + score in an event (rank may be null pre-snapshot).</summary>
public sealed class GauntletRankScore
{
    public int? Rank { get; init; }
    public long Score { get; init; }
}
