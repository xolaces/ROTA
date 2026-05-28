using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerInventoryRepository
{
    Task<IReadOnlyList<PlayerInventoryItem>> GetAllForPlayerAsync(Guid playerId, CancellationToken ct = default);
    Task<PlayerInventoryItem?> GetAsync(Guid playerId, string itemDefinitionId, CancellationToken ct = default);
    Task CreateAsync(PlayerInventoryItem item, CancellationToken ct = default);
    Task UpdateAsync(PlayerInventoryItem item, CancellationToken ct = default);
}
