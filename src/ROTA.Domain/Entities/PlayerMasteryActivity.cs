namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// A per-player running tally for one <see cref="MasteryActivityType"/> (System 22 Phase A). The
/// mastery challenge checklists compare these cumulative counters against per-tier thresholds. One
/// row per (player, activity_type); the race-safe increment in Slice 4 uses a raw ON CONFLICT upsert
/// (the unique index is the conflict target), so this entity's <see cref="Add"/> is for the EF/test path.
/// </summary>
public class PlayerMasteryActivity
{
    // Required by EF Core
    private PlayerMasteryActivity() { }

    public static PlayerMasteryActivity Create(Guid playerId, MasteryActivityType activityType, long initial = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlayerMasteryActivity
        {
            Id           = Guid.NewGuid(),
            PlayerId     = playerId,
            ActivityType = activityType,
            Counter      = initial,
            CreatedAt    = now,
            UpdatedAt    = now,
            IsDeleted    = false,
        };
    }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public MasteryActivityType ActivityType { get; private set; }
    public long Counter { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public void Add(long delta)
    {
        if (delta <= 0) return;
        Counter += delta;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
