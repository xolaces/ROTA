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
}
