using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IRaidService
{
    /// <summary>Returns all non-expired, non-defeated raids with the caller's damage fields populated.</summary>
    Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Creates a new ActiveRaid from the specified JSON definition.</summary>
    Task<SummonRaidResult> SummonRaidAsync(Guid playerId, string raidDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Resolves a single raid hit: validates state, spends stamina, computes server-side damage,
    /// upserts participation, distributes kill rewards if HP reaches 0.
    /// Idempotency enforced via Redis — duplicate idempotencyKey returns cached response immediately.
    /// </summary>
    Task<RaidHitResult> HitRaidAsync(Guid playerId, Guid activeRaidId, int hitSize, string idempotencyKey, CancellationToken ct = default);
}
