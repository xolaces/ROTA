using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public record RaidParticipantRank(Guid PlayerId, string DisplayName, long TotalDamageDealt, int HitCount);

public interface IRaidParticipantRepository
{
    Task<RaidParticipant?> FindByRaidAndPlayerAsync(Guid activeRaidId, Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<RaidParticipant>> GetAllForRaidAsync(Guid activeRaidId, CancellationToken ct = default);
    Task<IReadOnlyList<RaidParticipant>> GetCompletedForPlayerAsync(Guid playerId, int limit, CancellationToken ct = default);
    Task<RaidParticipant> CreateAsync(RaidParticipant participant, CancellationToken ct = default);
    Task UpdateAsync(RaidParticipant participant, CancellationToken ct = default);

    /// <summary>
    /// Top participants of a raid by total damage, descending; ties broken by earliest UpdatedAt.
    /// Joins players to resolve DisplayName. Excludes soft-deleted participants. Caps at `top` rows.
    /// </summary>
    Task<IReadOnlyList<RaidParticipantRank>> GetTopByDamageAsync(Guid activeRaidId, int top, CancellationToken ct = default);
}
