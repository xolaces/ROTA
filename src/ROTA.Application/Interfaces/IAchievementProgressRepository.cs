using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IAchievementProgressRepository
{
    /// <summary>
    /// EVERY progress row for the player, completed or not. The achievement overview needs all of
    /// them; the completion sweep does not — see <see cref="GetIncompleteForPlayerAsync"/>.
    /// </summary>
    Task<IReadOnlyList<AchievementProgress>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Only the rows still in progress. <c>EvaluateCompletionsAsync</c> runs on every quest attempt and
    /// acts solely on incomplete rows, so fetching the completed ones just to skip them in memory is
    /// waste on the hottest path in the game — and waste that grows monotonically, since a row becomes
    /// completed and then stays that way forever.
    /// </summary>
    Task<IReadOnlyList<AchievementProgress>> GetIncompleteForPlayerAsync(Guid playerId, CancellationToken ct = default);

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
