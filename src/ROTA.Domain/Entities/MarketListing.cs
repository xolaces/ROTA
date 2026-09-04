using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

/// <summary>
/// One consignment on the player market. System 27 (prototype).
///
/// THE LISTING HOLDS THE GOODS. Creating a listing REMOVES the stack from the seller's inventory and
/// parks it here; cancelling or expiring gives it back; a sale hands it to the buyer. Nothing is ever
/// in two places, so there is no window in which a seller can use, craft with, or re-list something
/// that is already on offer — the class of bug that ends game economies.
///
/// THE STATUS IS THE LATCH. A sale is a conditional UPDATE guarded on <c>status = Active</c>, so two
/// buyers pressing at once produce exactly one winner: the loser's UPDATE matches no row and is told
/// the listing is gone, before any gold moves. Same discipline as
/// <c>BetaKeyRepository.TryRedeemAsync</c> and the raid loot latch.
///
/// THERE IS NO DIRECT PLAYER-TO-PLAYER TRANSFER anywhere in this model, deliberately. A blind
/// consignment desk cannot express "give my alt this item for one gold" as a private act — every
/// transfer is a public listing at a public price that anyone may take first. That is the single
/// cheapest structural defence against muling, account-selling and the "trade window" scam, and it is
/// worth more than any amount of after-the-fact detection.
/// </summary>
public class MarketListing
{
    private MarketListing() { }

    public static MarketListing Create(
        Guid sellerId,
        MarketItemKind kind,
        string definitionId,
        int quantity,
        long unitPrice,
        DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new MarketListing
        {
            Id           = Guid.NewGuid(),
            SellerId     = sellerId,
            Kind         = kind,
            DefinitionId = definitionId,
            Quantity     = quantity,
            UnitPrice    = unitPrice,
            Status       = MarketListingStatus.Active,
            ListedAt     = now,
            ExpiresAt    = expiresAt,
            CreatedAt    = now,
            UpdatedAt    = now,
            IsDeleted    = false,
        };
    }

    public Guid                 Id           { get; private set; }
    public Guid                 SellerId     { get; private set; }
    public MarketItemKind       Kind         { get; private set; }
    public string               DefinitionId { get; private set; } = string.Empty;
    public int                  Quantity     { get; private set; }

    /// <summary>Gold per unit. The total the buyer pays is <c>UnitPrice * Quantity</c>.</summary>
    public long                 UnitPrice    { get; private set; }

    public MarketListingStatus  Status       { get; private set; }
    public DateTimeOffset       ListedAt     { get; private set; }

    /// <summary>
    /// When the offer lapses. An expired listing is settled by returning the goods, not by deleting
    /// them — a clock that runs out and returns nothing is the recurring defect this codebase has
    /// already been bitten by twice (World raid expiry, Gauntlet event close).
    /// </summary>
    public DateTimeOffset       ExpiresAt    { get; private set; }

    public Guid?                BuyerId      { get; private set; }
    public DateTimeOffset?      SoldAt       { get; private set; }

    /// <summary>Total gold the buyer paid, recorded at sale so a later price edit cannot rewrite history.</summary>
    public long                 SalePrice    { get; private set; }

    /// <summary>Gold taken by the house on the sale. Recorded here as well as in the ledger.</summary>
    public long                 SaleFeePaid  { get; private set; }

    public DateTimeOffset       CreatedAt    { get; private set; }
    public DateTimeOffset       UpdatedAt    { get; private set; }
    public bool                 IsDeleted    { get; private set; }

    public long TotalPrice => UnitPrice * Quantity;

    /// <summary>
    /// Marks the listing sold. Returns false when it was not Active — the caller must treat that as
    /// "someone else got it" rather than retrying, because the goods have already left.
    /// </summary>
    public bool TrySell(Guid buyerId, long salePrice, long feePaid)
    {
        if (Status != MarketListingStatus.Active) return false;
        Status      = MarketListingStatus.Sold;
        BuyerId     = buyerId;
        SoldAt      = DateTimeOffset.UtcNow;
        SalePrice   = salePrice;
        SaleFeePaid = feePaid;
        UpdatedAt   = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Withdraws an unsold listing. False when it is no longer Active.</summary>
    public bool TryCancel()
    {
        if (Status != MarketListingStatus.Active) return false;
        Status    = MarketListingStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Lapses an unsold listing whose clock ran out. False when it is no longer Active.</summary>
    public bool TryExpire()
    {
        if (Status != MarketListingStatus.Active) return false;
        Status    = MarketListingStatus.Expired;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }
}
