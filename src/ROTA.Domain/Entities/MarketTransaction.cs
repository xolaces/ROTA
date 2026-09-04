using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

/// <summary>
/// The append-only trade ledger. One row per completed sale, carrying both sides, the price and the
/// tax taken — the shape <c>docs/design/PLAYER_MARKET.md</c> §4 names as a prerequisite, and the same
/// discipline as <c>gem_transactions</c> and <c>audit_log</c>.
///
/// WHY A SEPARATE LEDGER WHEN THE LISTING ALREADY RECORDS THE SALE. Because the listing is mutable
/// and the ledger is not. A listing row can be corrected, migrated or soft-deleted; a ledger row is
/// the thing an investigator trusts when asking where 400 million gold came from. It is also the only
/// place the two per-day caps can be enforced honestly, since it is the sole record of gold that
/// actually changed hands rather than gold that was merely asked for.
///
/// Everything here is denormalised on purpose. If a definition id is retired from content, or a
/// player is deleted, the row still says what happened.
/// </summary>
public class MarketTransaction
{
    private MarketTransaction() { }

    public static MarketTransaction Create(
        Guid listingId,
        Guid sellerId,
        Guid buyerId,
        MarketItemKind kind,
        string definitionId,
        int quantity,
        long unitPrice,
        long totalPrice,
        long saleFee,
        long listingFee,
        long sellerProceeds)
    {
        var now = DateTimeOffset.UtcNow;
        return new MarketTransaction
        {
            Id             = Guid.NewGuid(),
            ListingId      = listingId,
            SellerId       = sellerId,
            BuyerId        = buyerId,
            Kind           = kind,
            DefinitionId   = definitionId,
            Quantity       = quantity,
            UnitPrice      = unitPrice,
            TotalPrice     = totalPrice,
            SaleFee        = saleFee,
            ListingFee     = listingFee,
            SellerProceeds = sellerProceeds,
            OccurredAt     = now,
            CreatedAt      = now,
            UpdatedAt      = now,
            IsDeleted      = false,
        };
    }

    public Guid           Id             { get; private set; }
    public Guid           ListingId      { get; private set; }
    public Guid           SellerId       { get; private set; }
    public Guid           BuyerId        { get; private set; }
    public MarketItemKind Kind           { get; private set; }
    public string         DefinitionId   { get; private set; } = string.Empty;
    public int            Quantity       { get; private set; }
    public long           UnitPrice      { get; private set; }

    /// <summary>What the buyer paid, in full.</summary>
    public long           TotalPrice     { get; private set; }

    /// <summary>Gold destroyed by the sale tax. This is the sink; it goes nowhere.</summary>
    public long           SaleFee        { get; private set; }

    /// <summary>Gold destroyed up front when the listing was created, repeated here so one row tells the whole story.</summary>
    public long           ListingFee     { get; private set; }

    /// <summary>What the seller actually received: TotalPrice - SaleFee.</summary>
    public long           SellerProceeds { get; private set; }

    public DateTimeOffset OccurredAt     { get; private set; }
    public DateTimeOffset CreatedAt      { get; private set; }
    public DateTimeOffset UpdatedAt      { get; private set; }
    public bool           IsDeleted      { get; private set; }
}
