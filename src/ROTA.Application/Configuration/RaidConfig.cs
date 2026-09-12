namespace ROTA.Application.Configuration;

public class RaidConfig
{
    // How often the expiry sweeper looks for timer-only (World) raids whose clock has run out.
    // A World raid runs for days, so settling within a minute of expiry is ample; the sweep is a cheap
    // indexed query that returns nothing on almost every tick.
    public int ExpirySweepSeconds { get; set; } = 60;

    // Cap on raids settled per tick. Bounds both the memory a single sweep can pull in and how long one
    // tick can hold advisory locks. A backlog simply drains across the following ticks, oldest first.
    public int ExpirySweepBatchSize { get; set; } = 50;

    // How long after a defeated raid's clock would have run out its unclaimed loot stays claimable.
    // Past this the sweeper removes the raid and every participation row on it; whatever was never
    // claimed is forfeited. A claimed row never waits for this — it is removed as it is claimed.
    public int LootClaimDays { get; set; } = 7;
}
