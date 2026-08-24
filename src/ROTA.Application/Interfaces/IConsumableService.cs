using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// D-008 / D-013 — the premium half of the consumable escape valve. Gold-priced potions are ITEMS and
/// live on <see cref="IItemService"/>; the instant full refill is a SERVICE purchase with no inventory
/// row, so it lives here.
/// </summary>
public interface IConsumableService
{
    /// <summary>What an instant refill costs and whether the caller can currently use it.</summary>
    Task<RefillOptionsResponse> GetRefillOptionsAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Spends gems to fill <paramref name="resourceType"/> to its maximum. Rejects when the pool is
    /// already full rather than charging for nothing.
    /// </summary>
    Task<RefillResourceResponse> RefillAsync(
        Guid playerId, ResourceType resourceType, CancellationToken ct = default);
}
