using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public sealed class ItemService : IItemService
{
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IItemDefinitionProvider _itemDefs;
    private readonly IRaidDefinitionProvider _raidDefs;   // resolves a sigil's summon-target raid tier
    private readonly IStatService _stats;
    private readonly IRaidService _raids;
    private readonly IAuditLogRepository _auditLog;
    private readonly IPlayerMutationLock _mutationLock;   // exploit audit 2026-06-14 (E)
    private readonly IEnergyService _energy;              // D-008 consumables — resource restore
    private readonly IPlayerResourceRepository _resources; // reads pool live/max for the response
    private readonly IPlayerRepository _players;           // D-013 gold shop — conditional debit

    public ItemService(
        IPlayerInventoryRepository inventory,
        IItemDefinitionProvider itemDefs,
        IRaidDefinitionProvider raidDefs,
        IStatService stats,
        IRaidService raids,
        IAuditLogRepository auditLog,
        IPlayerMutationLock mutationLock,
        IEnergyService energy,
        IPlayerResourceRepository resources,
        IPlayerRepository players)
    {
        _inventory = inventory;
        _itemDefs  = itemDefs;
        _raidDefs  = raidDefs;
        _stats     = stats;
        _raids     = raids;
        _auditLog  = auditLog;
        _mutationLock = mutationLock;
        _energy    = energy;
        _resources = resources;
        _players   = players;
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
                SummonRaidId     = def.SummonRaidId,
                SummonDifficulty = def.SummonDifficulty,
                // Resolve the summon-target raid's tier so the client doesn't hardcode "World raid"
                // for every sigil (a Standard zone-boss sigil now reports "Standard"). Null for
                // non-sigil items or an unresolvable target.
                Tier             = def.SummonRaidId is null
                                       ? null
                                       : _raidDefs.GetById(def.SummonRaidId)?.Tier,
                // D-008 consumables: the effect travels with the item so the client can state what
                // a potion does, and warn when using it would be wasted.
                RestoreResourceType = def.RestoreResourceType,
                RestoreAmount       = def.RestoreAmount,
                RestoreToMax        = def.RestoreToMax,
                StatPointsOnUse     = def.StatPointsOnUse,
                GoldPrice           = def.GoldPrice,
            });
        }
        return result;
    }

    // SECURITY (exploit audit 2026-06-14, finding E): serialize per-player so concurrent uses of a single
    // item can't each run the side effect (StatBag SP / Sigil summon) while only one copy is consumed.
    public Task<UseItemResponse> UseItemAsync(
        Guid playerId, string itemDefinitionId, int quantity, CancellationToken ct = default)
    {
        // Authoritative guard (defense-in-depth behind the validator): a non-positive quantity must
        // never reach ConsumeQuantity, which would otherwise ADD inventory (Quantity -= -1) and grant
        // negative stat points. Cheap reject before taking the lock.
        if (quantity < 1)
            return Task.FromResult(UseFail(UseItemFailureCode.InsufficientItems, "Quantity must be at least 1."));

        return _mutationLock.RunAsync(playerId, () => UseItemCoreAsync(playerId, itemDefinitionId, quantity, ct), ct);
    }

    private async Task<UseItemResponse> UseItemCoreAsync(
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
        string? resourceRestored = null;
        int restoredAmount = 0, resourceNewValue = 0, resourceMaxValue = 0;

        switch (def.Type)
        {
            // D-008 / northstar §1 — the consumable escape valve. Runs inside the per-player mutation
            // lock (like StatBag/Sigil), so a concurrent double-use can't restore twice off one item.
            case ItemType.Consumable when def.RestoreResourceType is not null:
            {
                if (!Enum.TryParse<ResourceType>(def.RestoreResourceType, out var restoreType))
                    return UseFail(UseItemFailureCode.ItemNotUsable,
                        "Consumable has invalid resource configuration.");

                var pool = await _resources.GetAsync(playerId, restoreType, ct);
                if (pool is null)
                    return UseFail(UseItemFailureCode.ItemNotUsable,
                        $"You have no {restoreType} pool to restore.");

                // Live value, not the stored checkpoint — regen since the last write counts toward "full".
                var before = await _energy.GetCurrentEnergyAsync(playerId, restoreType, ct);
                if (before >= pool.MaxValue)
                    return UseFail(UseItemFailureCode.ResourceAlreadyFull,
                        $"Your {restoreType} is already full.");

                if (def.RestoreToMax)
                {
                    // A full refill consumes exactly one — anything beyond the first is pure waste, and
                    // silently eating the extras would read as theft.
                    if (quantity != 1)
                        return UseFail(UseItemFailureCode.ItemNotUsable,
                            "A full-refill consumable can only be used one at a time.");
                    await _energy.RefillToMaxAsync(playerId, restoreType, ct);
                }
                else
                {
                    if (def.RestoreAmount <= 0)
                        return UseFail(UseItemFailureCode.ItemNotUsable,
                            "Consumable restores nothing.");
                    // Overfill is clamped by RefillEnergyAsync; the response reports what actually landed.
                    await _energy.RefillEnergyAsync(playerId, restoreType, def.RestoreAmount * quantity, ct);
                }

                resourceNewValue = await _energy.GetCurrentEnergyAsync(playerId, restoreType, ct);
                resourceMaxValue = pool.MaxValue;
                restoredAmount   = resourceNewValue - before;
                resourceRestored = restoreType.ToString();
                break;
            }

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
            $"Used {quantity}x {def.Name} ({itemDefinitionId}). StatPoints: {statPointsGranted}"
                + (resourceRestored is null ? "" : $". Restored: {restoredAmount} {resourceRestored}"),
            null), ct);

        return new UseItemResponse
        {
            Success          = true,
            ItemDefinitionId = itemDefinitionId,
            QuantityConsumed = quantity,
            RemainingQuantity = inv.Quantity,
            StatPointsGranted = statPointsGranted,
            RaidSummoned     = raidSummoned,
            ResourceRestored = resourceRestored,
            ResourceAmountRestored = restoredAmount,
            ResourceNewValue = resourceNewValue,
            ResourceMaxValue = resourceMaxValue,
        };
    }

    private static UseItemResponse UseFail(UseItemFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };

    // ── Consumable shop (D-008 / D-013) ────────────────────────────────────────────────────────

    /// <summary>
    /// Upper bound on one purchase. Keeps GoldPrice * quantity far inside long and stops a
    /// fat-fingered quantity draining a balance in a single call — not a gameplay cap.
    /// </summary>
    private const int MaxPurchaseQuantity = 1000;

    public async Task<ShopCatalogueResponse> GetShopAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        var gold = player?.Gold ?? 0;

        // One inventory read for the whole catalogue rather than a lookup per row.
        var owned = (await _inventory.GetAllForPlayerAsync(playerId, ct))
            .ToDictionary(i => i.ItemDefinitionId, i => i.Quantity, StringComparer.Ordinal);

        var rows = _itemDefs.GetAll()
            .Where(d => d.Type == ItemType.Consumable && d.GoldPrice > 0)
            .OrderBy(d => d.RestoreResourceType, StringComparer.Ordinal)
            .ThenBy(d => d.GoldPrice)
            .Select(d => new ShopItemResponse
            {
                ItemDefinitionId    = d.Id,
                Name                = d.Name,
                Description         = d.Description,
                Rarity              = d.Rarity.ToString(),
                ArtKey              = d.ArtKey,
                GoldPrice           = d.GoldPrice,
                RestoreResourceType = d.RestoreResourceType ?? string.Empty,
                RestoreAmount       = d.RestoreAmount,
                RestoreToMax        = d.RestoreToMax,
                QuantityOwned       = owned.TryGetValue(d.Id, out var q) ? q : 0,
                CanAfford           = gold >= d.GoldPrice,
            })
            .ToList();

        return new ShopCatalogueResponse { Items = rows, PlayerGold = gold };
    }

    public Task<BuyItemResponse> BuyItemAsync(
        Guid playerId, string itemDefinitionId, int quantity, CancellationToken ct = default)
    {
        // Cheap rejects before taking the lock. A non-positive quantity would otherwise produce a
        // negative cost — i.e. sell gold TO the player.
        if (quantity < 1)
            return Task.FromResult(BuyFail(BuyItemFailureCode.InvalidQuantity, "Quantity must be at least 1."));
        if (quantity > MaxPurchaseQuantity)
            return Task.FromResult(BuyFail(BuyItemFailureCode.InvalidQuantity,
                $"Quantity may not exceed {MaxPurchaseQuantity} per purchase."));

        return _mutationLock.RunAsync(playerId, () => BuyItemCoreAsync(playerId, itemDefinitionId, quantity, ct), ct);
    }

    private async Task<BuyItemResponse> BuyItemCoreAsync(
        Guid playerId, string itemDefinitionId, int quantity, CancellationToken ct = default)
    {
        var def = _itemDefs.GetById(itemDefinitionId);
        if (def is null)
            return BuyFail(BuyItemFailureCode.ItemNotFound, "Item definition not found.");
        // Only gold-priced consumables sell here. Equipment/sigils/materials have their own paths, and
        // a 0 price means drop-only (the full-refill elixir) — never purchasable.
        if (def.Type != ItemType.Consumable || def.GoldPrice <= 0)
            return BuyFail(BuyItemFailureCode.NotForSale, "This item is not sold for gold.");

        long totalCost = def.GoldPrice * quantity;   // bounded by MaxPurchaseQuantity — cannot overflow long

        // Gold is a COLUMN, not a ledger. Unlike gems and gauntlet currency there is no referenceId to
        // make a replay idempotent, so the tri-state spend-then-idempotent-grant pattern the other shops
        // use does not transfer. Instead the debit is a CONDITIONAL UPDATE that re-checks the balance in
        // the same statement (mirroring the gem ledger's SUM guard), so a read-then-write race can never
        // drive gold negative — no separate affordability read to go stale. It runs inside the mutation
        // lock's transaction along with the grant below, so both commit or neither does.
        var newGold = await _players.TrySpendGoldAsync(playerId, totalCost, ct);
        if (newGold is null)
            return BuyFail(BuyItemFailureCode.InsufficientGold,
                $"Insufficient gold. Required: {totalCost}.");

        var existing = await _inventory.GetAsync(playerId, itemDefinitionId, ct);
        int newQuantity;
        if (existing is not null)
        {
            existing.AddQuantity(quantity);
            await _inventory.UpdateAsync(existing, ct);
            newQuantity = existing.Quantity;
        }
        else
        {
            var created = PlayerInventoryItem.Create(playerId, itemDefinitionId, quantity);
            await _inventory.CreateAsync(created, ct);
            newQuantity = quantity;
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "ItemPurchased", null,
            $"Bought {quantity}x {def.Name} ({itemDefinitionId}) for {totalCost} gold. " +
            $"Gold now {newGold.Value}.", null), ct);

        return new BuyItemResponse
        {
            Success           = true,
            ItemDefinitionId  = itemDefinitionId,
            QuantityPurchased = quantity,
            GoldSpent         = totalCost,
            NewPlayerGold     = newGold.Value,
            NewQuantityOwned  = newQuantity,
        };
    }

    private static BuyItemResponse BuyFail(BuyItemFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
