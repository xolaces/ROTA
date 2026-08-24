using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerUnitRepository
{
    Task<IReadOnlyList<PlayerUnit>> GetOwnedAsync(Guid playerId, CancellationToken ct = default);
    Task<PlayerUnit?> FindAsync(Guid playerId, string unitDefinitionId, CancellationToken ct = default);
    Task<PlayerUnit> UpsertAsync(Guid playerId, string unitDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Persists mutations to an already-loaded row. Added for System 26 crafting (D-018), which is the
    /// first caller to take a unit AWAY — every other path only ever grants.
    /// </summary>
    Task UpdateAsync(PlayerUnit unit, CancellationToken ct = default);
}
