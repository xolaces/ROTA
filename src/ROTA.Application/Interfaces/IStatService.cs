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
        Guid playerId, int amount, CancellationToken ct = default);

    Task<PlayerStatsResponse?> GetStatsAsync(
        Guid playerId, CancellationToken ct = default);

    int XpToNextLevel(int level);
}
