namespace ROTA.Shared.DTOs;

public class InventoryItemResponse
{
    public string ItemDefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ArtKey { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    // TICKET-2-061126 — sigil summon target, hydrated from the item definition so the summon
    // screen can group sigils by boss + difficulty without parsing ids. Null for non-sigils.
    public string? SummonRaidId { get; set; }
    public string? SummonDifficulty { get; set; }
    // Raid tier of the sigil's summon target (Standard | World | Event | Guild), so the client boss
    // card shows the right label instead of a hardcoded "World raid". Null for non-sigil items.
    public string? Tier { get; set; }
}

public class UseItemResponse
{
    public bool Success { get; set; }
    public UseItemFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public string ItemDefinitionId { get; set; } = string.Empty;
    public int QuantityConsumed { get; set; }
    public int RemainingQuantity { get; set; }
    public int StatPointsGranted { get; set; }
    public SummonRaidResponse? RaidSummoned { get; set; }

    // Consumables (D-008): what the potion actually restored, so the client can update the HUD bar
    // from the response instead of re-fetching the profile. Null/0 for non-consumables.
    public string? ResourceRestored { get; set; }
    public int ResourceAmountRestored { get; set; }
    public int ResourceNewValue { get; set; }
    public int ResourceMaxValue { get; set; }
}

public class ItemGrantDTO
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Rarity { get; set; } = string.Empty;
    public string ArtKey { get; set; } = string.Empty;
}


public class UseItemRequest
{
    public int Quantity { get; set; } = 1;
}

public enum UseItemFailureCode
{
    None              = 0,
    ItemNotFound      = 1,
    InsufficientItems = 2,
    ItemNotUsable     = 3,
    RaidSummonFailed  = 4,
    // Consumables (D-008): the target pool is already full — reject rather than silently burn the
    // item for zero benefit. A wasted potion reads as a bug to the player.
    ResourceAlreadyFull = 5,
}

// ── Consumable shop (D-008 / D-013) ────────────────────────────────────────────────────────────
// Gold is the price rail for consumables: it is the game's soft currency and, before this, had
// exactly one lifetime sink (guild creation). Gem-priced instant refills are a separate path.

/// <summary>One purchasable row in the consumable shop.</summary>
public class ShopItemResponse
{
    public string ItemDefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public string ArtKey { get; set; } = string.Empty;
    public long GoldPrice { get; set; }

    /// <summary>Which pool this restores (Energy/Stamina/Health) — drives the shop's grouping.</summary>
    public string RestoreResourceType { get; set; } = string.Empty;

    /// <summary>Points restored per unit; 0 when <see cref="RestoreToMax"/> is set.</summary>
    public int RestoreAmount { get; set; }
    public bool RestoreToMax { get; set; }

    /// <summary>How many the caller already holds, so the shop can show "owned" without a second call.</summary>
    public int QuantityOwned { get; set; }

    /// <summary>False when the caller cannot currently afford one unit.</summary>
    public bool CanAfford { get; set; }
}

/// <summary>The consumable shop plus the caller's spending power.</summary>
public class ShopCatalogueResponse
{
    public List<ShopItemResponse> Items { get; set; } = new();
    public long PlayerGold { get; set; }
}

public class BuyItemRequest
{
    public int Quantity { get; set; } = 1;
}

public class BuyItemResponse
{
    public bool Success { get; set; }
    public BuyItemFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public string ItemDefinitionId { get; set; } = string.Empty;
    public int QuantityPurchased { get; set; }
    public long GoldSpent { get; set; }
    /// <summary>Balance after the purchase, so the client updates the header without a refetch.</summary>
    public long NewPlayerGold { get; set; }
    /// <summary>Total held after the grant.</summary>
    public int NewQuantityOwned { get; set; }
}

public enum BuyItemFailureCode
{
    None             = 0,
    ItemNotFound     = 1,
    NotForSale       = 2,
    InvalidQuantity  = 3,
    InsufficientGold = 4,
}
