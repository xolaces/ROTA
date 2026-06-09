using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IAchievementProgressRepository
{
    Task<IReadOnlyList<AchievementProgress>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default);

    Task<AchievementProgress?> FindAsync(Guid playerId, string achievementId, CancellationToken ct = default);

    /// <summary>
    /// Race-safe cumulative increment via raw ON CONFLICT upsert (clones PlayerMasteryActivityRepository).
    /// Participates in the ambient transaction (e.g. the raid advisory-lock tx) when one is open.
    /// </summary>
    Task IncrementAsync(Guid playerId, string achievementId, long delta, CancellationToken ct = default);

    /// <summary>
    /// Sets the counter to an ABSOLUTE value via raw ON CONFLICT upsert (recount metrics —
    /// EquipmentPiecesOwned / CollectorItemCount — so a re-grant never inflates a drifting total).
    /// </summary>
    Task SetCounterAsync(Guid playerId, string achievementId, long absoluteValue, CancellationToken ct = default);

    /// <summary>Persists a new or changed progress row (by Id) — used to latch IsCompleted/CompletedAt.</summary>
    Task UpsertAsync(AchievementProgress progress, CancellationToken ct = default);
}
