namespace ROTA.Application.Models;

public class RaidDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "Standard";    // Standard | World | Event | Guild
    public long BaseHp { get; set; }
    // HP used when a Sigil summons a Personal-size raid.  0 = fall back to BaseHp.
    public long PersonalBaseHp { get; set; }
    public int TimerHours { get; set; }
    public int StaminaCostPerHit { get; set; } = 1;
    public string LootTableId { get; set; } = string.Empty;
    public long BaseGoldReward { get; set; }
    public int BaseExperienceReward { get; set; }
    public int BaseGemReward { get; set; }
    public bool HasOnHitDrops { get; set; }
    public string ArtKey { get; set; } = string.Empty;
}
