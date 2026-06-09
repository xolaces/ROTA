namespace ROTA.Domain.Entities;

/// <summary>
/// Append-only idempotency ledger for <b>exactly-once</b> achievement-progress seams (TICKET 46):
/// when a referenced activity (e.g. a per-day login, a per-(raid,player) kill) records progress, a
/// row is written here BEFORE the aggregate counter increment, so a replay never double-counts that
/// achievement. Naturally-replay-safe seams pass no referenceId and skip this ledger. Unique on
/// (player_id, achievement_id, reference_id). Mirrors <c>MasteryActivityEvent</c>: no
/// updated_at / is_deleted — a recorded progress event is permanent.
/// </summary>
public class AchievementProgressEvent
{
    // Required by EF Core
    private AchievementProgressEvent() { }

    public static AchievementProgressEvent Create(Guid playerId, string achievementId, string referenceId)
        => new()
        {
            Id            = Guid.NewGuid(),
            PlayerId      = playerId,
            AchievementId = achievementId,
            ReferenceId   = referenceId,
            CreatedAt     = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string AchievementId { get; private set; } = string.Empty;
    public string ReferenceId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
