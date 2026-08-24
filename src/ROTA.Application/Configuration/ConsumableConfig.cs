namespace ROTA.Application.Configuration;

/// <summary>
/// D-008 / D-013 — the consumable escape valve's tunables. Potions are content (priced in
/// <c>items.json</c> as gold); this holds only the GEM-priced instant refill, which is a service, not
/// an item, so it has no content row to live in.
/// </summary>
public class ConsumableConfig
{
    /// <summary>
    /// Gem cost of one instant full refill, per resource. Keyed by <c>ResourceType</c> name
    /// (Energy / Stamina / Health). A resource absent from this map is NOT refillable for gems —
    /// that is how GuildStamina stays out of the premium path.
    /// </summary>
    public Dictionary<string, int> InstantRefillGemCost { get; set; } = new();

    /// <summary>
    /// Idempotency bucket, in seconds, for the refill's gem-ledger referenceId. Two refill requests
    /// for the same player+resource inside one bucket share a referenceId, so a retry after a dropped
    /// response re-runs the (idempotent) fill instead of charging twice. The per-player mutation lock
    /// plus the already-full rejection already cover a double-click; this covers the crash/retry case
    /// that spans requests. Deliberately short — a player who genuinely spends a pool dry and refills
    /// again within the bucket would otherwise be refilled for free.
    /// </summary>
    public int RefillIdempotencyWindowSeconds { get; set; } = 10;
}
