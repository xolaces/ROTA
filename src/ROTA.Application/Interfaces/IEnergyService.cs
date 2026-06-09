using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IEnergyService
{
    Task<int> GetCurrentEnergyAsync(Guid playerId, ResourceType type, CancellationToken ct = default);

    Task<bool> SpendEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default);

    Task RefillEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default);

    /// <summary>
    /// T56 — deducts up to <paramref name="amount"/> from the pool, clamping at 0 (never fails).
    /// Used for health damage (drain rather than reject). Returns the amount actually drained.
    /// </summary>
    Task<int> DrainAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default);

    /// <summary>
    /// Fully refills the resource pool to its current MaxValue (CurrentValue = MaxValue),
    /// resetting the regen checkpoint to now. Used on level-up (all pools restored).
    /// </summary>
    Task RefillToMaxAsync(Guid playerId, ResourceType type, CancellationToken ct = default);

    Task UpdateMaxAsync(Guid playerId, ResourceType type, int newMax, CancellationToken ct = default);

    /// <summary>
    /// Returns the class-based regen rate (minutes to regenerate one point) for the given
    /// class and resource type. Delegates to ClassConfig — no DB call required.
    /// </summary>
    double GetRegenMinutesPerPoint(PlayerClass playerClass, ResourceType type);
}
