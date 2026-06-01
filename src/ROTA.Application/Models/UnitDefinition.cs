using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class UnitDefinition
{
    public string       Id          { get; set; } = string.Empty;
    public string       Name        { get; set; } = string.Empty;
    public string       Description { get; set; } = string.Empty;
    public UnitType     UnitType    { get; set; }
    public ItemRarity   Rarity      { get; set; }
    public int          BaseAttack  { get; set; }
    public int          BaseDefense { get; set; }
    public UnitRace     Race        { get; set; }
    public UnitRole     Role        { get; set; }
    public UnitAttribute Attribute  { get; set; }
    // null = no ability (isPassive units or plain troops)
    public UnitAbility? Ability     { get; set; }
    // When Ability is non-null, whether it is a passive stat/legion-bonus effect rather than a
    // rollable DamageProc. Passive abilities are folded into legionPower; they must NOT roll in
    // the proc phase (doing so would double-count them).
    public bool         IsPassive   { get; set; }
    // % added to the legion's bonus fraction; generals only (0 for troops)
    public double       LegionBonus { get; set; }
    public string       IconPath    { get; set; } = string.Empty;
    public string       Acquisition { get; set; } = string.Empty;
}
