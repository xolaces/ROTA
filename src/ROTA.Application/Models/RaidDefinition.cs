namespace ROTA.Application.Models;

/// <summary>
/// Represents a raid boss loaded from content/raids.json at startup.
/// Static game content — not stored in the database.
/// </summary>
public class RaidDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "World";
    public long HpPool { get; set; }
    public int TimerHours { get; set; }
    public int StaminaCostPerHit { get; set; } = 1;
    public string LootTableId { get; set; } = string.Empty;
    public long GoldReward { get; set; }
    public int ExperienceReward { get; set; }
    public int GemReward { get; set; }
    public long MinContributionForLoot { get; set; }
    public string ArtKey { get; set; } = string.Empty;
}
