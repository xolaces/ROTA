using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class GearDefinition
{
    public string      Id          { get; set; } = string.Empty;
    public string      Name        { get; set; } = string.Empty;
    public string      Description { get; set; } = string.Empty;
    public ItemRarity  Rarity      { get; set; }
    public string      Slot        { get; set; } = string.Empty; // stored as string, parsed to EquipmentSlot
    public int         BonusAttack  { get; set; }
    public int         BonusDefense { get; set; }
    public double?     ProcChance   { get; set; } // null = no proc
    public double?     ProcPercent  { get; set; } // bonus added = baseDamage × ProcPercent
    public string      IconPath     { get; set; } = string.Empty;
    public List<ConditionalBonus> ConditionalBonuses { get; set; } = new();
}
