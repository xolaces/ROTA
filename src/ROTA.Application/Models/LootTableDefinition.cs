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
    public List<ItemDropChance>? GuaranteedDrops { get; set; }
    public List<ItemDropChance>? ChanceDrops { get; set; }

    // Raid loot table fields
    public double MinContributionPercent { get; set; }
    public List<ThresholdReward>? ThresholdRewards { get; set; }
    public List<ItemDropChance>? OnHitDrops { get; set; }
}

public class ThresholdReward
{
    public double ContributionPercent { get; set; }
    public int UnassignedStatPoints { get; set; }
    public int AttackPoints { get; set; }
    public int DefensePoints { get; set; }
    public int DiscernmentPoints { get; set; }
    public List<ItemDropChance> ItemDrops { get; set; } = new();
}

public class ItemDropChance
{
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public double Chance { get; set; } = 1.0;
}
