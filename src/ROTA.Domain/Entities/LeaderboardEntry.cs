using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

// BETA — aggregate row: one per player × board × period_key.
// Sum boards (EnergySpent, DamageDealt) call AddValue; Max board (LargestHit) calls MaxValue.
// Stat boards (StatAttack/StatDefense/StatDiscernment) overwrite via the repository's upsert;
// they do not call these domain methods.
public class LeaderboardEntry
{
    // Required by EF Core
    private LeaderboardEntry() { }

    /// <summary>
    /// Factory — creates a new leaderboard entry for a player on a board+period.
    /// <paramref name="at"/> is stored as <see cref="LastProgressAt"/> (tiebreak source).
    /// </summary>
    public static LeaderboardEntry Create(
        Guid playerId,
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string periodKey,
        long initialValue,
        DateTimeOffset at)
        => new LeaderboardEntry
        {
            Id             = Guid.NewGuid(),
            PlayerId       = playerId,
            Board          = board,
            Period         = period,
            PeriodKey      = periodKey,
            Value          = initialValue,
            LastProgressAt = at,
            Rank           = null,
            CreatedAt      = at,
            UpdatedAt      = at,
            IsDeleted      = false,
        };

    public Guid Id { get; private set; }

    public Guid PlayerId { get; private set; }

    /// <summary>Which leaderboard this entry belongs to.</summary>
    public LeaderboardBoard Board { get; private set; }

    /// <summary>Window granularity (Daily/Weekly/Monthly/Live).</summary>
    public LeaderboardPeriod Period { get; private set; }

    /// <summary>
    /// Deterministic calendar bucket key produced by <see cref="Interfaces.IPeriodKeyResolver"/>.
    /// Examples: "day:2026-06-02", "week:2026-W23", "month:2026-06", "live".
    /// </summary>
    public string PeriodKey { get; private set; } = default!;

    /// <summary>Accumulated value — sum for Sum boards, best for Max boards.</summary>
    public long Value { get; private set; }

    /// <summary>
    /// Tiebreak field: the UTC instant this row's value last moved upward.
    /// Earliest-to-reach wins when two players share the same Value.
    /// </summary>
    public DateTimeOffset LastProgressAt { get; private set; }

    /// <summary>
    /// Denormalized snapshot rank. Null in v1 (RankRefresh=OnRead); populated only when
    /// <see cref="SetRank"/> is called during a future snapshot-mode refresh.
    /// </summary>
    public int? Rank { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    // ── Domain methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Sum board: adds <paramref name="delta"/> to Value and moves LastProgressAt forward.
    /// </summary>
    /// <param name="delta">Must be &gt;= 0. Negative deltas are silently ignored.</param>
    /// <param name="at">UTC instant of this contribution.</param>
    public void AddValue(long delta, DateTimeOffset at)
    {
        if (delta <= 0)
            return; // guard — no negative increments; zero is a no-op

        Value          += delta;
        LastProgressAt = at;
        UpdatedAt      = at;
    }

    /// <summary>
    /// Max board: updates Value only when <paramref name="candidate"/> exceeds the current
    /// Value. LastProgressAt moves forward only when the value actually rises.
    /// </summary>
    /// <param name="candidate">New candidate value.</param>
    /// <param name="at">UTC instant of this contribution.</param>
    /// <returns><c>true</c> if Value was raised; <c>false</c> if candidate did not exceed current Value.</returns>
    public bool MaxValue(long candidate, DateTimeOffset at)
    {
        if (candidate <= Value)
            return false;

        Value          = candidate;
        LastProgressAt = at;
        UpdatedAt      = at;
        return true;
    }

    /// <summary>
    /// Stores a precomputed rank on the row (used by snapshot-mode refresh; a no-op in v1's
    /// on-read ranking mode but kept so the infrastructure is in place).
    /// </summary>
    public void SetRank(int rank)
    {
        Rank      = rank;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
