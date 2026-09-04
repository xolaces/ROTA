namespace ROTA.Domain.Enums;

/// <summary>
/// A listing's lifecycle. Active is the only state in which goods and gold can move, and every
/// transition out of it is guarded on being IN it — that guard is the concurrency control for the
/// whole market.
/// </summary>
public enum MarketListingStatus
{
    Active    = 0,
    Sold      = 1,
    Cancelled = 2,
    Expired   = 3,
}
