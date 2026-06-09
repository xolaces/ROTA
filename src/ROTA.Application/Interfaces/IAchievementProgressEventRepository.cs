using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Append-only progress-event idempotency ledger (TICKET 46). A referenced progress increment writes
/// a row here FIRST; if the (player, achievement, referenceId) row already exists the increment is
/// skipped. Mirrors <c>MasteryActivityEvent</c>'s repo: <see cref="CreateAsync"/> is
/// unique-violation-idempotent (returns false on a concurrent dup).
/// </summary>
public interface IAchievementProgressEventRepository
{
    /// <summary>True if a progress event with this (player, achievement, referenceId) already exists.</summary>
    Task<bool> ExistsAsync(Guid playerId, string achievementId, string referenceId, CancellationToken ct = default);

    /// <summary>
    /// Inserts the progress event. Returns false if (player_id, achievement_id, reference_id) already
    /// exists (concurrent dup) — clones the mastery respec / achievement award unique-violation create.
    /// </summary>
    Task<bool> CreateAsync(AchievementProgressEvent progressEvent, CancellationToken ct = default);
}
