namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// Append-only idempotency ledger for <b>exactly-once</b> mastery activity seams only (System 22
/// Phase A, Slice 4): a row is written before the aggregate counter increment for referenced
/// activities (Gauntlet rank settled, per-level level-up), so a re-settle / replayed level-up never
/// double-counts. Naturally-replay-safe seams (raid hit, quest clear, contribution) pass no
/// referenceId and skip this ledger. Unique on (player_id, activity_type, reference_id).
/// </summary>
public class MasteryActivityEvent
{
    // Required by EF Core
    private MasteryActivityEvent() { }

    public static MasteryActivityEvent Create(Guid playerId, MasteryActivityType activityType, string referenceId)
        => new()
        {
            Id           = Guid.NewGuid(),
            PlayerId     = playerId,
            ActivityType = activityType,
            ReferenceId  = referenceId,
            CreatedAt    = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public MasteryActivityType ActivityType { get; private set; }
    public string ReferenceId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
