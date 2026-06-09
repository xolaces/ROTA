using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IAchievementAwardRepository
{
    /// <summary>Total Achievement Points = SUM(points) over the player's award ledger (never a stored field).</summary>
    Task<int> GetTotalPointsAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>True if an award row with this (player, referenceId) already exists (idempotency pre-check).</summary>
    Task<bool> ReferenceExistsAsync(Guid playerId, string referenceId, CancellationToken ct = default);

    /// <summary>Append-only award rows for the player (read for the overview's per-achievement state).</summary>
    Task<IReadOnlyList<AchievementAward>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Inserts the award row. Returns false if (player_id, achievement_id) already exists (concurrent
    /// dup) — clones the mastery respec repo's unique-violation-idempotent create.
    /// </summary>
    Task<bool> CreateAsync(AchievementAward award, CancellationToken ct = default);
}
