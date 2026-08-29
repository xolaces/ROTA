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
    public List<MagicDropChance>?  MagicDrops      { get; set; }
    public List<UnitDropChance>?   UnitDrops       { get; set; }
    public List<LegionDropChance>? LegionDrops     { get; set; }
    public List<GearDropChance>?   GearDrops       { get; set; }

    // Raid loot table fields
    public double MinContributionPercent { get; set; }
    public List<ThresholdReward>? ThresholdRewards { get; set; }
    public List<ItemDropChance>?  OnHitDrops       { get; set; }
}

public class ThresholdReward
{
    public double ContributionPercent { get; set; }

    /// <summary>
    /// ABSOLUTE damage required to bank this rung, used INSTEAD of <see cref="ContributionPercent"/>
    /// on a timer-only raid (owner 2026-08-29: World raids have no collective health and pay on
    /// "raid dmg being the tier reward").
    ///
    /// A share of the total is meaningless before a raid ends — there is no total yet — and a ladder
    /// a player can see mid-event is worth more than one they can only compute afterwards. 0 means
    /// this rung is percentage-keyed, which is every campaign raid.
    /// </summary>
    public long DamageThreshold { get; set; }
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
