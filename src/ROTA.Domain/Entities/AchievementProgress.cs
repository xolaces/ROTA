namespace ROTA.Domain.Entities;

/// <summary>
/// A per-player running tally toward one achievement (TICKET 46). One row per
/// (player, achievement_id); lazily created on first activity. <see cref="ProgressValue"/> is the
/// tracked counter (cumulative for delta metrics, absolute for the recount metrics). When the
/// threshold is first crossed the achievement is awarded once via the append-only
/// <see cref="AchievementAward"/> ledger, and <see cref="IsCompleted"/>/<see cref="CompletedAt"/>
/// latch here. Mirrors <c>PlayerMasteryActivity</c>; the race-safe increment uses a raw ON CONFLICT
/// upsert (the unique index is the conflict target), so <see cref="Add"/> is for the EF/test path.
/// </summary>
public class AchievementProgress
{
    // Required by EF Core
    private AchievementProgress() { }

    public static AchievementProgress Create(Guid playerId, string achievementId, long initial = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new AchievementProgress
        {
            Id            = Guid.NewGuid(),
            PlayerId      = playerId,
            AchievementId = achievementId,
            ProgressValue = initial,
            IsCompleted   = false,
            CompletedAt   = null,
            CreatedAt     = now,
            UpdatedAt     = now,
            IsDeleted     = false,
        };
    }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string AchievementId { get; private set; } = string.Empty;
    public long ProgressValue { get; private set; }
    public bool IsCompleted { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    /// <summary>Adds a positive delta to the counter (cumulative metrics). No-op on a non-positive delta.</summary>
    public void Add(long delta)
    {
        if (delta <= 0) return;
        ProgressValue += delta;
        Touch();
    }

    /// <summary>Sets the counter to an absolute value (recount metrics — EquipmentPiecesOwned / CollectorItemCount).</summary>
    public void SetValue(long value)
    {
        if (value < 0) value = 0;
        if (value == ProgressValue) return;
        ProgressValue = value;
        Touch();
    }

    /// <summary>Latches the completion flag + timestamp the first time the threshold is crossed. Idempotent.</summary>
    public void MarkComplete(DateTimeOffset when)
    {
        if (IsCompleted) return;
        IsCompleted = true;
        CompletedAt = when;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
