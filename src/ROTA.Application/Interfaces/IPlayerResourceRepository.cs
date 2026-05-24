using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IPlayerResourceRepository
{
    Task<PlayerResource?> GetAsync(Guid playerId, ResourceType type, CancellationToken ct = default);

    /// <summary>
    /// Acquires a row-level lock (FOR UPDATE), calls <paramref name="updateFn"/> with the locked
    /// entity, and commits if the function returns true. Returns false if the resource does not
    /// exist or <paramref name="updateFn"/> returns false.
    /// SECURITY: prevents double-spend race conditions without optimistic retry loops.
    /// </summary>
    Task<bool> AtomicUpdateAsync(
        Guid playerId,
        ResourceType type,
        Func<PlayerResource, bool> updateFn,
        CancellationToken ct = default);
}
