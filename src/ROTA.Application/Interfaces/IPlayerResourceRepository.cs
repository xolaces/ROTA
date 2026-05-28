using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IPlayerResourceRepository
{
    Task<PlayerResource?> GetAsync(Guid playerId, ResourceType type, CancellationToken ct = default);

    Task<bool> AtomicUpdateAsync(
        Guid playerId,
        ResourceType type,
        Func<PlayerResource, bool> updateFn,
        CancellationToken ct = default);
}
