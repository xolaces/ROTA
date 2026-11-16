using ROTA.Domain.Enums;

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

    // Max HP by raid size, on top of the difficulty multiplier, for raids summoned through
    // RaidService.SummonRaidAsync (owner, 2026-11-16). Size used to change only the player cap, so a
    // Titanic raid had the HP of a Small one. Personal stays 1: its base is already PersonalBaseHp,
    // which content sets to a quarter of BaseHp. Guild raids (always Large, for the member cap),
    // Gauntlet stages and World raids (MaxHp 0) do not read this. Raids already alive keep their HP.
    public Dictionary<string, double> HpMultiplierBySize { get; set; } = new()
    {
        ["Personal"] = 1.0,
        ["Small"]    = 1.0,
        ["Medium"]   = 2.5,
        ["Large"]    = 5.0,
        ["Titanic"]  = 20.0,
    };

    /// <summary>The size's HP multiplier; 1 when the size is missing or set to zero or less.</summary>
    public double HpMultiplierFor(RaidSize size)
        => HpMultiplierBySize.TryGetValue(size.ToString(), out var m) && m > 0 ? m : 1.0;

    // Per-raid multiplier on the content base HP (BaseHp, or PersonalBaseHp for Personal), applied
    // before difficulty and size, for raids summoned through RaidService.SummonRaidAsync (owner,
    // 2026-11-16). Lets the server rebase a raid's health without a content change. A raid not listed
    // keeps its content value. Guild raids, Gauntlet stages and World raids do not read this. Raids
    // already alive keep their HP.
    public Dictionary<string, double> BaseHpMultiplierByRaid { get; set; } = new();

    /// <summary>The raid's base HP multiplier; 1 when the raid is not listed or set to zero or less.</summary>
    public double BaseHpMultiplierFor(string raidDefinitionId)
        => BaseHpMultiplierByRaid.TryGetValue(raidDefinitionId, out var m) && m > 0 ? m : 1.0;
}
