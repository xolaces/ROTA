using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IRaidService
{
    Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(Guid playerId, CancellationToken ct = default);

    Task<IReadOnlyList<CompletedRaidResponse>> GetCompletedRaidsAsync(Guid playerId, CancellationToken ct = default);

    Task<SummonRaidResult> SummonRaidAsync(
        Guid playerId, string raidDefinitionId, RaidDifficulty difficulty,
        RaidSize size = RaidSize.Large, CancellationToken ct = default);

    Task<RaidHitResult> HitRaidAsync(
        Guid playerId, Guid activeRaidId, int hitSize, string idempotencyKey, CancellationToken ct = default);

    // Join-by-UID lookup. Returns the raid mapped to ActiveRaidResponse regardless of IsPublic
    // (the GUID is the invite token). Returns null when not found / deleted / defeated / expired,
    // or when it's a Personal raid the caller did not summon (avoids leaking others' solo raids).
    Task<ActiveRaidResponse?> GetRaidByIdAsync(Guid activeRaidId, Guid callerId, CancellationToken ct = default);

    // Summoner-only publish to the public list. Sets IsPublic, writes audit_log, returns the
    // updated raid. Fails NotFound (missing/expired), NotSummoner, or CannotSharePersonal.
    Task<ShareRaidResult> ShareRaidAsync(Guid callerId, Guid activeRaidId, CancellationToken ct = default);

    Task<IReadOnlyList<RaidParticipantRankDto>> GetParticipantsAsync(Guid activeRaidId, int top, CancellationToken ct = default);
}
