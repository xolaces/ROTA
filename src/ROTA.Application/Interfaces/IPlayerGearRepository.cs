using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerGearRepository
{
    // Owned, undeleted rows with quantity > 0 — the player's gear bag.
    Task<IReadOnlyList<PlayerGear>> GetOwnedAsync(Guid playerId, CancellationToken ct = default);
    // Any row regardless of IsDeleted — used for the acquisition upsert.
    Task<PlayerGear?> GetAsync(Guid playerId, string gearDefinitionId, CancellationToken ct = default);
    Task CreateAsync(PlayerGear gear, CancellationToken ct = default);
    Task UpdateAsync(PlayerGear gear, CancellationToken ct = default);
}
