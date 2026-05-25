using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IRaidParticipantRepository
{
    Task<RaidParticipant?> FindByRaidAndPlayerAsync(Guid activeRaidId, Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<RaidParticipant>> GetAllForRaidAsync(Guid activeRaidId, CancellationToken ct = default);
    Task<RaidParticipant> CreateAsync(RaidParticipant participant, CancellationToken ct = default);
    Task UpdateAsync(RaidParticipant participant, CancellationToken ct = default);
}
