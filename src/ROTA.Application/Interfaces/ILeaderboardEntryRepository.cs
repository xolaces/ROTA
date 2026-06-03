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

    // ── Slice 3: eligibility-aware ranked reads ──────────────────────────────
    //
    // The Slice-2 GetPageAsync filters only leaderboard_entry.is_deleted.  The methods below
    // JOIN players and apply the full eligibility predicate IN SQL so that paging offsets and
    // rank counts are correct — post-filtering in memory would corrupt both.
    //
    // Eligibility predicate (applied server-side in every query below):
    //   p.is_deleted = false
    //   AND p.is_banned = false
    //   AND p.level >= @minLevel
    //   AND (@excludeAdmins = false OR (p.roles & @adminBit) = 0)
    // Moderator bit is not excluded — Moderators appear as regular players.

    /// <summary>
    /// Returns a page of ELIGIBLE entries ordered value DESC, last_progress_at ASC.
    /// Eligibility is enforced in SQL via a JOIN on the players table.
    /// </summary>
    Task<IReadOnlyList<EligibleLeaderboardEntry>> GetEligiblePageAsync(
        LeaderboardBoard board,
        string periodKey,
        int page,
        int pageSize,
        int minLevel,
        bool excludeAdmins,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the total count of ELIGIBLE entries for a board+period_key.
    /// Used to populate <c>TotalRanked</c> in the page response.
    /// </summary>
    Task<int> CountEligibleAsync(
        LeaderboardBoard board,
        string periodKey,
        int minLevel,
        bool excludeAdmins,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the caller's own entry (value + rank) on a board+period_key.
    /// Rank is computed as (count of eligible entries ranked strictly above the caller) + 1,
    /// where "above" = value &gt; callerValue OR (value = callerValue AND last_progress_at &lt; callerLastProgressAt).
    /// Returns <c>null</c> if the caller has no entry or is themselves ineligible.
    /// </summary>
    Task<CallerRankEntry?> GetCallerRankAsync(
        Guid callerId,
        LeaderboardBoard board,
        string periodKey,
        int minLevel,
        bool excludeAdmins,
        CancellationToken ct = default);

    // ── Slice 5: overwrite-upsert for Stat board snapshot ───────────────────
    //
    // Stat boards (Period=Live) are refreshed by an admin/CLI snapshot that overwrites
    // each player's row with their current raw stat value.  This is distinct from:
    //   - IncrementAsync  (Sum boards — accumulates delta)
    //   - MaxUpdateAsync  (Max boards — only raises)
    // SetValueAsync overwrites unconditionally, but preserves last_progress_at when the
    // value has NOT changed (so the earliest-to-reach tiebreak survives repeated snapshots).

    /// <summary>
    /// Overwrite-upsert for Stat boards (Period=Live, period_key="live").
    /// <para>On first call: inserts a row with Value = <paramref name="value"/>.</para>
    /// <para>On conflict: overwrites Value unconditionally (snapshot semantics).
    /// <c>last_progress_at</c> is bumped to <paramref name="at"/> ONLY when the value actually
    /// changed; if the value is unchanged the existing <c>last_progress_at</c> is kept so the
    /// earliest-to-reach tiebreak survives repeated snapshots of the same stat score.</para>
    /// </summary>
    Task SetValueAsync(
        Guid playerId,
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string periodKey,
        long value,
        DateTimeOffset at,
        CancellationToken ct = default);

    /// <summary>
    /// Returns every eligible player's Id, BaseAttack, BaseDefense, and DiscernmentInvestment
    /// in a single SQL projection — used by <c>SnapshotStatBoardAsync</c> to avoid
    /// loading full entity graphs.
    /// Eligibility predicate is identical to <see cref="GetEligiblePageAsync"/>:
    /// not banned, not soft-deleted, level >= <paramref name="minLevel"/>,
    /// and (when <paramref name="excludeAdmins"/> is true) Admin role bit not set.
    /// </summary>
    Task<IReadOnlyList<EligibleStatSnapshot>> GetEligibleStatSnapshotAsync(
        int minLevel,
        bool excludeAdmins,
        CancellationToken ct = default);
}

/// <summary>
/// A lightweight projection of one eligible player's raw stat values for the Stat-board snapshot.
/// </summary>
public sealed class EligibleStatSnapshot
{
    public Guid PlayerId             { get; init; }
    public int  BaseAttack           { get; init; }
    public int  BaseDefense          { get; init; }
    public int  DiscernmentInvestment { get; init; }
}

/// <summary>
/// A leaderboard entry hydrated with the player's display name for a ranked page.
/// Produced by the eligibility-aware SQL join.
/// </summary>
public sealed class EligibleLeaderboardEntry
{
    public Guid PlayerId { get; init; }
    public long Value { get; init; }
    public DateTimeOffset LastProgressAt { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
}

/// <summary>The caller's own rank and value on a specific board+period.</summary>
public sealed class CallerRankEntry
{
    public Guid PlayerId { get; init; }
    public long Value { get; init; }
    public int Rank { get; init; }
}
