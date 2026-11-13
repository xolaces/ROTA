using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IStatService
{
    Task<AllocateStatResponse> AllocateStatPointAsync(
        Guid playerId, StatType statType, int amount, CancellationToken ct = default);

    Task GrantLevelUpPointsAsync(
        Guid playerId, int newLevel, CancellationToken ct = default);

    Task AddUnassignedPointsAsync(
        Guid playerId, long amount, CancellationToken ct = default);

    /// <summary>
    /// Allocates several stats as one unit: every cost and the LSI cap are checked against the whole
    /// set first, and nothing is spent unless all of it can be. The response describes the final state.
    /// </summary>
    Task<AllocateStatResponse> AllocateStatPointsAsync(
        Guid playerId, IReadOnlyList<(StatType Stat, int Amount)> allocations, CancellationToken ct = default);

    Task<PlayerStatsResponse?> GetStatsAsync(
        Guid playerId, CancellationToken ct = default);

    int XpToNextLevel(int level);

    CritProfile GetCritProfile(long discernment);
}
