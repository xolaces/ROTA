namespace ROTA.Application.Models;

public enum ConditionType
{
    OwnedUnitCount, // floor(count(target_id) / perCount) × bonusAmount
    OwnedTypeCount, // floor(sum(items with tag=target) / perCount) × bonusAmount
    EquippedSlot,   // binary: 1 if slot occupied, 0 if empty; perCount=1 always
}

public enum BonusType
{
    FlatAttack,
    FlatDefense,
    ProcChanceFlat,    // accumulated value clamped to 1.0 before proc roll
    ProcAmountFlat,
    FlatDamagePercent, // multiplied into damageFinal after crit, before TakeDamage
}

public class ConditionalBonus
{
    public ConditionType ConditionType   { get; set; }
    public string        ConditionTarget { get; set; } = string.Empty;
    public int           PerCount        { get; set; } = 1;
    public BonusType     BonusType       { get; set; }
    public double        BonusAmount     { get; set; }
}
