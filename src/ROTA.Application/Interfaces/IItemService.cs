using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IItemService
{
    Task<IReadOnlyList<InventoryItemResponse>> GetInventoryAsync(Guid playerId, CancellationToken ct = default);
    Task<UseItemResponse> UseItemAsync(Guid playerId, string itemDefinitionId, int quantity, CancellationToken ct = default);
}
