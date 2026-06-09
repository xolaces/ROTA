namespace ROTA.Domain.Entities;

/// <summary>
/// Append-only ledger of awarded achievement points (TICKET 46). Total AP is always SUM(points)
/// over this table — never a stored field (the gem-ledger discipline). Exactly one award row per
/// achievement per player, enforced by the unique (player_id, achievement_id) index; the
/// <see cref="ReferenceId"/> carries the same key for an explicit idempotency pre-check. No
/// updated_at / is_deleted — like <c>MasteryActivityEvent</c>, an awarded point is permanent.
/// </summary>
public class AchievementAward
{
    // Required by EF Core
    private AchievementAward() { }

    public static AchievementAward Create(Guid playerId, string achievementId, int points, string referenceId)
        => new()
        {
            Id            = Guid.NewGuid(),
            PlayerId      = playerId,
            AchievementId = achievementId,
            Points        = points,
            ReferenceId   = referenceId,
            CreatedAt     = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string AchievementId { get; private set; } = string.Empty;
    public int Points { get; private set; }
    public string ReferenceId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
