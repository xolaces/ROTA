using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IRaidService
{
    Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(Guid playerId, CancellationToken ct = default);

    Task<SummonRaidResult> SummonRaidAsync(
        Guid playerId, string raidDefinitionId, RaidDifficulty difficulty,
        RaidSize size = RaidSize.Large, CancellationToken ct = default);

    Task<RaidHitResult> HitRaidAsync(
        Guid playerId, Guid activeRaidId, int hitSize, string idempotencyKey, CancellationToken ct = default);
}
