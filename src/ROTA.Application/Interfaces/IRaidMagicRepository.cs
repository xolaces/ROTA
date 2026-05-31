using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IRaidMagicRepository
{
    Task<IReadOnlyList<RaidMagic>> GetForRaidAsync(Guid activeRaidId, CancellationToken ct = default);
    Task<int> CountForRaidAsync(Guid activeRaidId, CancellationToken ct = default);
    Task<RaidMagic?> FindAsync(Guid activeRaidId, string magicDefinitionId, CancellationToken ct = default);
    Task<RaidMagic?> FindByPlayerAsync(Guid activeRaidId, Guid playerId, CancellationToken ct = default);
    Task<RaidMagic> CreateAsync(RaidMagic raidMagic, CancellationToken ct = default);
    Task SoftDeleteAsync(RaidMagic raidMagic, CancellationToken ct = default);
}
