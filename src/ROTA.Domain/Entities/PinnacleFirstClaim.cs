namespace ROTA.Domain.Entities;

/// <summary>
/// Records the FIRST player to reach a given pinnacle level post-launch (T33). Exactly one row per
/// pinnacle level — a unique index on <see cref="PinnacleLevel"/> enforces the "first" semantics
/// atomically at the database, so concurrent level-ups can't both claim the same level.
/// </summary>
public class PinnacleFirstClaim
{
    // Required by EF Core
    private PinnacleFirstClaim() { }

    public static PinnacleFirstClaim Create(int pinnacleLevel, Guid playerId)
        => new PinnacleFirstClaim
        {
            Id = Guid.NewGuid(),
            PinnacleLevel = pinnacleLevel,
            PlayerId = playerId,
            ClaimedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false,
        };

    public Guid Id { get; private set; }

    /// <summary>The pinnacle level that was first reached.</summary>
    public int PinnacleLevel { get; private set; }

    /// <summary>The player who reached it first.</summary>
    public Guid PlayerId { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; }
}
