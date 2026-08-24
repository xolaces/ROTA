using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IItemService
{
    Task<IReadOnlyList<InventoryItemResponse>> GetInventoryAsync(Guid playerId, CancellationToken ct = default);
    Task<UseItemResponse> UseItemAsync(Guid playerId, string itemDefinitionId, int quantity, CancellationToken ct = default);

    /// <summary>The gold-priced consumable shop, hydrated with the caller's gold and holdings (D-008/D-013).</summary>
    Task<ShopCatalogueResponse> GetShopAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Buys <paramref name="quantity"/> of a gold-priced consumable. The gold debit and the inventory
    /// grant commit together under the per-player mutation lock, so a lost race charges nothing.
    /// </summary>
    Task<BuyItemResponse> BuyItemAsync(Guid playerId, string itemDefinitionId, int quantity, CancellationToken ct = default);
}
