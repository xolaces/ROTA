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
}
