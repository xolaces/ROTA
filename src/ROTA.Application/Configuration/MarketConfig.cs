namespace ROTA.Application.Configuration;

/// <summary>
/// System 27 — the player market's tunables. Every number here is a lever on an economy that, once
/// open, is very hard to close, so the defaults are deliberately the CAUTIOUS end of what
/// <c>docs/design/PLAYER_MARKET.md</c> recommends: that doc argues a combined 10-20% tax is normal
/// for the genre and would do real work, then cautions that "a market nobody uses bridges nobody" and
/// that raising a fee is far easier to explain than lowering one and needing it back. So the shipped
/// default is a combined 10%, at the bottom of the band, with room to climb.
/// </summary>
public class MarketConfig
{
    /// <summary>
    /// Master switch. FALSE by default: a market is not a feature that should arrive because someone
    /// forgot to turn it off. Every endpoint refuses while this is false.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Charged when a listing is CREATED, on the asking price, and never refunded — not on a cancel,
    /// not on an expiry. That is the whole point: it prices the act of occupying the board, so
    /// price-probing and spam listings cost something. Fraction, not percent.
    /// </summary>
    public double ListingFeeRate { get; set; } = 0.02;

    /// <summary>Taken out of the seller's proceeds when a sale completes. Fraction, not percent.</summary>
    public double SaleFeeRate { get; set; } = 0.08;

    /// <summary>
    /// The kinds of thing that may be listed, by <c>ItemType</c> name for items. Consumables are
    /// ABSENT on purpose: PLAYER_MARKET.md §5 shows that a market selling potions turns gold into a
    /// second faucet on the pacing curve and makes the autolevelling threshold purchasable. StatBags
    /// are absent because they are raw progression. Sigils are absent because they summon raids, which
    /// makes them a service rather than an object.
    /// </summary>
    public List<string> TradeableItemTypes { get; set; } = new() { "Material" };

    /// <summary>Whether gear may be listed. Gear is the market's reason to exist for most players.</summary>
    public bool GearTradeable { get; set; } = true;

    /// <summary>
    /// Definition ids that may never be listed whatever their type — the escape hatch for a specific
    /// chase item the owner wants to stay earned rather than bought. Nothing is bind-on-pickup in the
    /// item model yet (PLAYER_MARKET.md open question 5), so this list is the stand-in.
    /// </summary>
    public List<string> Untradeable { get; set; } = new();

    // ── Anti-mule gates. None of these stop a determined operator; together they make the cheap
    //    version of the attack uneconomic, which is the realistic goal.

    /// <summary>Minimum level to list or buy. The cheapest anti-mule measure available.</summary>
    public int MinLevelToTrade { get; set; } = 20;

    /// <summary>Minimum account age before trading, in hours. Blunts farm-and-dump on fresh accounts.</summary>
    public int MinAccountAgeHours { get; set; } = 48;

    /// <summary>How many listings one player may have Active at once.</summary>
    public int MaxActiveListingsPerPlayer { get; set; } = 10;

    /// <summary>
    /// Ceiling on gold one account may RECEIVE from sales in a rolling 24 hours. This is the cap that
    /// actually matters: a mule transfer is a sale at an absurd price, and no per-listing price band
    /// can distinguish that from a genuinely valuable item. A daily ceiling can.
    /// </summary>
    public long MaxGoldReceivedPerDay { get; set; } = 50_000_000;

    /// <summary>Ceiling on gold one account may SPEND on purchases in a rolling 24 hours.</summary>
    public long MaxGoldSpentPerDay { get; set; } = 50_000_000;

    /// <summary>Floor on a listing's unit price, so the board cannot be papered with 1-gold noise.</summary>
    public long MinUnitPrice { get; set; } = 100;

    /// <summary>Ceiling on a listing's unit price.</summary>
    public long MaxUnitPrice { get; set; } = 1_000_000_000;

    /// <summary>Most units one listing may carry.</summary>
    public int MaxQuantityPerListing { get; set; } = 1000;

    /// <summary>How long a listing stands before it lapses and the goods go home.</summary>
    public int ListingDurationHours { get; set; } = 48;

    /// <summary>Page size for browse. Bounded so a browse cannot materialise the board.</summary>
    public int BrowsePageSize { get; set; } = 50;
}
