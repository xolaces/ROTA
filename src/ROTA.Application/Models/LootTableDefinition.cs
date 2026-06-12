namespace ROTA.Application.Models;

public class LootTableDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;  // "Raid" | "QuestBoss"
    public Dictionary<string, LootTableDifficulty>? Difficulties { get; set; }
}

public class LootTableDifficulty
{
    // Quest loot table fields
    public List<ItemDropChance>?   GuaranteedDrops { get; set; }
    public List<ItemDropChance>?   ChanceDrops     { get; set; }
    public List<MagicDropChance>?  MagicDrops      { get; set; }  // quest magic drops
    public List<UnitDropChance>?   UnitDrops       { get; set; }  // quest unit drops
    public List<LegionDropChance>? LegionDrops     { get; set; }  // quest legion drops
    public List<GearDropChance>?   GearDrops       { get; set; }  // quest gear drops

    // Raid loot table fields
    public double MinContributionPercent { get; set; }
    public List<ThresholdReward>? ThresholdRewards { get; set; }
    public List<ItemDropChance>?  OnHitDrops       { get; set; }
}

public class ThresholdReward
{
    public double ContributionPercent { get; set; }
    public int UnassignedStatPoints { get; set; }
    public int AttackPoints { get; set; }
    public int DefensePoints { get; set; }
    public int DiscernmentPoints { get; set; }
    public List<ItemDropChance>   ItemDrops   { get; set; } = new();
    public List<MagicDropChance>  MagicDrops  { get; set; } = new();
    public List<UnitDropChance>   UnitDrops   { get; set; } = new();
    public List<LegionDropChance> LegionDrops { get; set; } = new();
    public List<GearDropChance>   GearDrops   { get; set; } = new();
}

public class ItemDropChance
{
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public double Chance { get; set; } = 1.0;
}

public class MagicDropChance
{
    public string MagicId { get; set; } = string.Empty;
    public double Chance  { get; set; } = 1.0;
}

public class UnitDropChance
{
    public string UnitId { get; set; } = string.Empty;
    public double Chance { get; set; } = 1.0;
}

public class LegionDropChance
{
    public string LegionId { get; set; } = string.Empty;
    public double Chance   { get; set; } = 1.0;
}

public class GearDropChance
{
    public string GearDefinitionId { get; set; } = string.Empty;
    public int    Quantity         { get; set; } = 1;
    public double Chance           { get; set; } = 1.0;
    // Owner 2026-06-12 — chase-set drops (Pano): use the asymptotic rare curve
    // (QuestConfig.RareDrop*) instead of the generic Discernment multiplier.
    public bool   RareScaling      { get; set; }
}
