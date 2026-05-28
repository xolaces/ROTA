using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IEnergyService
{
    Task<int> GetCurrentEnergyAsync(Guid playerId, ResourceType type, CancellationToken ct = default);

    Task<bool> SpendEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default);

    Task RefillEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default);

    Task UpdateMaxAsync(Guid playerId, ResourceType type, int newMax, CancellationToken ct = default);
}
