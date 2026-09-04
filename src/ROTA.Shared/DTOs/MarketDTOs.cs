namespace ROTA.Shared.DTOs;

/// <summary>System 27 — player market wire shapes.</summary>
public class CreateListingRequest
{
    /// <summary>"Item" or "Gear".</summary>
    public string Kind { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public long UnitPrice { get; set; }
}

public enum MarketFailureCode
{
    None = 0,
    MarketDisabled = 1,
    NotFound = 2,
    NotYours = 3,
    NoLongerActive = 4,
    NotTradeable = 5,
    InsufficientQuantity = 6,
    ItemInUse = 7,
    PriceOutOfRange = 8,
    QuantityOutOfRange = 9,
    TooManyActiveListings = 10,
    LevelTooLow = 11,
    AccountTooNew = 12,
    InsufficientGold = 13,
    CannotBuyOwnListing = 14,
    DailyGoldReceivedCapReached = 15,
    DailySpendCapReached = 16,
    TradingRestricted = 17,
    UnknownDefinition = 18,
}

public class MarketListingResponse
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long UnitPrice { get; set; }
    public long TotalPrice { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public bool IsOwnListing { get; set; }
    public DateTimeOffset ListedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public class MarketBrowseResponse
{
    public List<MarketListingResponse> Listings { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long PlayerGold { get; set; }

    /// <summary>Fee rates as fractions, so the client can show the cost before the player commits.</summary>
    public double ListingFeeRate { get; set; }
    public double SaleFeeRate { get; set; }
}

public class CreateListingResponse
{
    public bool Success { get; set; }
    public MarketFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public Guid ListingId { get; set; }
    public long ListingFeePaid { get; set; }
    public long NewPlayerGold { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public class BuyListingResponse
{
    public bool Success { get; set; }
    public MarketFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long GoldSpent { get; set; }
    public long NewPlayerGold { get; set; }
}

public class CancelListingResponse
{
    public bool Success { get; set; }
    public MarketFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int QuantityReturned { get; set; }

    /// <summary>Always 0 — stated on the wire so the client can say so plainly rather than implying a refund.</summary>
    public long ListingFeeRefunded { get; set; }
}
