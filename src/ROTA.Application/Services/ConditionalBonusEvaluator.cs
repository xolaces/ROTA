using ROTA.Application.Models;

namespace ROTA.Application.Services;

// Shared evaluator — gear and future troops/legions both call this verbatim.
// Returns raw accumulated sums; callers apply any clamping (e.g. ProcChance ≤ 1.0).
public static class ConditionalBonusEvaluator
{
    public static AccumulatedBonuses Evaluate(
        IEnumerable<ConditionalBonus> bonuses,
        IReadOnlyDictionary<string, int> ownedById,   // itemDefId → quantity
        IReadOnlyDictionary<string, int> ownedByTag,  // tag → total quantity
        IReadOnlySet<string> equippedSlots)            // lower-invariant slot names
    {
        int    flatAtk        = 0;
        int    flatDef        = 0;
        double procChanceFlat = 0;
        double procAmountFlat = 0;
        double flatDmgPct     = 0;

        foreach (var b in bonuses)
        {
            if (b.PerCount <= 0) continue;

            int stacks = b.ConditionType switch
            {
                ConditionType.OwnedUnitCount =>
                    ownedById.TryGetValue(b.ConditionTarget, out int c) ? c / b.PerCount : 0,
                ConditionType.OwnedTypeCount =>
                    ownedByTag.TryGetValue(b.ConditionTarget, out int c) ? c / b.PerCount : 0,
                ConditionType.EquippedSlot =>
                    equippedSlots.Contains(b.ConditionTarget.ToLowerInvariant()) ? 1 : 0,
                _ => 0,
            };

            double gained = stacks * b.BonusAmount;
            switch (b.BonusType)
            {
                case BonusType.FlatAttack:         flatAtk        += (int)gained; break;
                case BonusType.FlatDefense:        flatDef        += (int)gained; break;
                case BonusType.ProcChanceFlat:     procChanceFlat += gained;      break;
                case BonusType.ProcAmountFlat:     procAmountFlat += gained;      break;
                case BonusType.FlatDamagePercent:  flatDmgPct     += gained;      break;
            }
        }

        return new AccumulatedBonuses(flatAtk, flatDef, procChanceFlat, procAmountFlat, flatDmgPct);
    }
}

public sealed record AccumulatedBonuses(
    int    FlatAttack,
    int    FlatDefense,
    double ProcChanceFlat,
    double ProcAmountFlat,
    double FlatDamagePercent);
