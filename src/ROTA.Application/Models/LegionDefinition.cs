using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class LegionDefinition
{
    public string         Id           { get; set; } = string.Empty;
    public string         Name         { get; set; } = string.Empty;
    public string         Description  { get; set; } = string.Empty;
    public ItemRarity     Rarity       { get; set; }
    // % power bonus applied to the legion's power computation (LegionPower formula)
    public double         PowerBonus   { get; set; }
    public List<SlotSpec> GeneralSlots { get; set; } = new();
    public List<SlotSpec> TroopSlots   { get; set; } = new();
    public string         IconPath     { get; set; } = string.Empty;
    public string         Acquisition  { get; set; } = string.Empty;
    // 0 = not for sale in the gem shop; >0 = gem cost to purchase.
    public int            GemPrice     { get; set; }

    /// <summary>
    /// What this legion is BUILT to fight. Keyed by <c>RaidTag</c> name, valued as a PERCENT bonus to
    /// the legion's raw power against a raid carrying that tag — so 25.0 is +25%.
    ///
    /// HIGHEST-ONLY, never summed. A raid can carry several tags (a Glutbound goblin warband is both
    /// Goblin and Shadow) and a legion can answer several, but only the best match applies. Summing
    /// would make a broad legion beat a specialised one at its own specialty, which is backwards, and
    /// it is the same rule the Gauntlet trophies already use for the same reason.
    ///
    /// Keys are validated at boot against the RaidTag enum. An affinity for <c>None</c> is refused:
    /// it would be a flat power bonus wearing a counter-play costume, and PowerBonus already exists.
    /// </summary>
    public Dictionary<string, double> TagAffinities { get; set; } = new();
}
