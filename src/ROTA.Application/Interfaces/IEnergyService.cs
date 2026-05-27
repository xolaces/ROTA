using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IEnergyService
{
    /// <summary>
    /// Returns the live resource value by computing regen since the last checkpoint.
    /// Never returns the raw stored CurrentValue.
    /// </summary>
    Task<int> GetCurrentEnergyAsync(Guid playerId, ResourceType type, CancellationToken ct = default);

    /// <summary>
    /// Deducts <paramref name="amount"/> from the live value using a row-level lock.
    /// Returns false if the player has insufficient energy.
    /// </summary>
    Task<bool> SpendEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default);

    /// <summary>
    /// Adds <paramref name="amount"/> to the live value, capped at MaxValue.
    /// </summary>
    Task RefillEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default);

    /// <summary>
    /// Updates the MaxValue for a resource pool (called after stat investment changes).
    /// Caps the live checkpoint at the new max to avoid over-refill.
    /// </summary>
    Task UpdateMaxAsync(Guid playerId, ResourceType type, int newMax, CancellationToken ct = default);
}
