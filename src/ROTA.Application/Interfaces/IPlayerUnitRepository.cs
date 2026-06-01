using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerUnitRepository
{
    Task<IReadOnlyList<PlayerUnit>> GetOwnedAsync(Guid playerId, CancellationToken ct = default);
    Task<PlayerUnit?> FindAsync(Guid playerId, string unitDefinitionId, CancellationToken ct = default);
    Task<PlayerUnit> UpsertAsync(Guid playerId, string unitDefinitionId, CancellationToken ct = default);
}
