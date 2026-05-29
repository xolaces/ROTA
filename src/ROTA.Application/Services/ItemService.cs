using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public sealed class ItemService : IItemService
{
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IItemDefinitionProvider _itemDefs;
    private readonly IStatService _stats;
    private readonly IRaidService _raids;
    private readonly IAuditLogRepository _auditLog;

    public ItemService(
        IPlayerInventoryRepository inventory,
        IItemDefinitionProvider itemDefs,
        IStatService stats,
        IRaidService raids,
        IAuditLogRepository auditLog)
    {
        _inventory = inventory;
        _itemDefs  = itemDefs;
        _stats     = stats;
        _raids     = raids;
        _auditLog  = auditLog;
    }

    public async Task<IReadOnlyList<InventoryItemResponse>> GetInventoryAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var items = await _inventory.GetAllForPlayerAsync(playerId, ct);
        var result = new List<InventoryItemResponse>(items.Count);
        foreach (var inv in items.Where(i => i.Quantity > 0))
        {
            var def = _itemDefs.GetById(inv.ItemDefinitionId);
            if (def is null) continue;
            result.Add(new InventoryItemResponse
            {
                ItemDefinitionId = inv.ItemDefinitionId,
                Name             = def.Name,
                Description      = def.Description,
                Rarity           = def.Rarity.ToString(),
                Type             = def.Type.ToString(),
                ArtKey           = def.ArtKey,
                Quantity         = inv.Quantity,
                AcquiredAt       = inv.AcquiredAt,
            });
        }
        return result;
    }

    public async Task<UseItemResponse> UseItemAsync(
        Guid playerId, string itemDefinitionId, int quantity, CancellationToken ct = default)
    {
        var def = _itemDefs.GetById(itemDefinitionId);
        if (def is null)
            return UseFail(UseItemFailureCode.ItemNotFound, "Item definition not found.");

        var inv = await _inventory.GetAsync(playerId, itemDefinitionId, ct);
        if (inv is null || inv.Quantity < quantity)
            return UseFail(UseItemFailureCode.InsufficientItems, "Insufficient quantity in inventory.");

        int statPointsGranted = 0;
        SummonRaidResponse? raidSummoned = null;

        switch (def.Type)
        {
            case ItemType.StatBag:
                int totalPoints = def.StatPointsOnUse * quantity;
                await _stats.AddUnassignedPointsAsync(playerId, totalPoints, ct);
                statPointsGranted = totalPoints;
                break;

            case ItemType.Sigil when def.SummonRaidId is not null && def.SummonDifficulty is not null:
                if (!Enum.TryParse<RaidDifficulty>(def.SummonDifficulty, out var raidDiff))
                    return UseFail(UseItemFailureCode.ItemNotUsable, "Sigil has invalid difficulty configuration.");

                // Sigils default to Personal; content can override via SummonSize.
                var raidSize = (def.SummonSize is not null
                    && Enum.TryParse<RaidSize>(def.SummonSize, out var parsedSize))
                    ? parsedSize : RaidSize.Personal;

                // Ordering: summon first, then consume.  A failed summon returns early (line below)
                // and leaves inventory untouched.  A successful summon followed by a consume failure
                // (e.g., SaveChanges throws) would grant a free raid — acceptable crash-risk for BETA;
                // Phase 2 wraps summon+consume in an explicit DB transaction.
                var summonResult = await _raids.SummonRaidAsync(playerId, def.SummonRaidId, raidDiff, raidSize, ct);
                if (!summonResult.Success)
                    return UseFail(UseItemFailureCode.RaidSummonFailed,
                        summonResult.FailureReason ?? "Raid summon failed.");
                raidSummoned = summonResult.Response;
                break;

            default:
                return UseFail(UseItemFailureCode.ItemNotUsable, "This item type cannot be used directly.");
        }

        inv.ConsumeQuantity(quantity);
        await _inventory.UpdateAsync(inv, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "ItemUsed", null,
            $"Used {quantity}x {def.Name} ({itemDefinitionId}). StatPoints: {statPointsGranted}",
            null), ct);

        return new UseItemResponse
        {
            Success          = true,
            ItemDefinitionId = itemDefinitionId,
            QuantityConsumed = quantity,
            RemainingQuantity = inv.Quantity,
            StatPointsGranted = statPointsGranted,
            RaidSummoned     = raidSummoned,
        };
    }

    private static UseItemResponse UseFail(UseItemFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
