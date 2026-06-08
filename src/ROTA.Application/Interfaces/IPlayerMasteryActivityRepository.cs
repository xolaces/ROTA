using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IPlayerMasteryActivityRepository
{
    Task<IReadOnlyList<PlayerMasteryActivity>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Race-safe cumulative increment via raw ON CONFLICT upsert (clones LeaderboardEntryRepository).
    /// Participates in the ambient transaction (e.g. the raid advisory-lock tx) when one is open.
    /// </summary>
    Task IncrementAsync(Guid playerId, MasteryActivityType activityType, long delta, CancellationToken ct = default);

    /// <summary>True if an idempotency event row already exists for (player, activityType, referenceId).</summary>
    Task<bool> HasEventAsync(Guid playerId, MasteryActivityType activityType, string referenceId, CancellationToken ct = default);

    /// <summary>Inserts the idempotency event row (caller pre-checks <see cref="HasEventAsync"/> to avoid collisions).</summary>
    Task RecordEventAsync(Guid playerId, MasteryActivityType activityType, string referenceId, CancellationToken ct = default);
}
