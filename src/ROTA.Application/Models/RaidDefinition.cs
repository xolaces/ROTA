namespace ROTA.Application.Models;

public class RaidDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "Standard";    // Standard | World | Event | Guild

    /// <summary>
    /// Danger class — Common | Deadly | Elite | Mythic. A LABEL, not a formula input: health is
    /// typed directly into raids.json, so grade drives display and nothing else. Keeping it out of
    /// the raid's NAME is deliberate — "Deadly Guardian of X" welds theme to mechanics and makes
    /// re-theming a rewrite, where a separate field is a find-replace.
    /// </summary>
    public string Grade { get; set; } = "Common";
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
    // Gold granted per stamina spent on every hit (on-hit reward, not kill reward)
    public long GoldPerStamina { get; set; } = 1;
}
