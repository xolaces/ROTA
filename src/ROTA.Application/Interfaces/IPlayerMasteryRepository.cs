using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IPlayerMasteryRepository
{
    Task<IReadOnlyList<PlayerMastery>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default);

    Task<PlayerMastery?> FindAsync(Guid playerId, MasteryAncient ancient, CancellationToken ct = default);

    /// <summary>Persists a new or changed mastery row (by Id).</summary>
    Task UpsertAsync(PlayerMastery mastery, CancellationToken ct = default);

    /// <summary>Ensures all four Ancients have a row (level 1) for the player; returns all four (tracked).</summary>
    Task<IReadOnlyList<PlayerMastery>> EnsureAllAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// All players' current mastery levels for the rating-board snapshot. Players with no mastery
    /// rows are omitted (they sit at the floor rating; the read path applies eligibility).
    /// </summary>
    Task<IReadOnlyList<PlayerMasteryRatingRow>> GetAllRatingsAsync(CancellationToken ct = default);
}

/// <summary>One player's mastery levels keyed by Ancient (missing Ancients default to level 1).</summary>
public sealed record PlayerMasteryRatingRow(Guid PlayerId, IReadOnlyDictionary<MasteryAncient, int> Levels);
