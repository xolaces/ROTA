using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerLegionRepository
{
    Task<IReadOnlyList<PlayerLegion>> GetOwnedAsync(Guid playerId, CancellationToken ct = default);
    Task<PlayerLegion?> FindAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default);
    Task<PlayerLegion?> GetActiveAsync(Guid playerId, CancellationToken ct = default);
    Task<PlayerLegion> UpsertAsync(Guid playerId, string legionDefinitionId, CancellationToken ct = default);
    Task UpdateAsync(PlayerLegion legion, CancellationToken ct = default);
}
