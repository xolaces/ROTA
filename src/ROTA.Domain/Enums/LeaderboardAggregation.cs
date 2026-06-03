namespace ROTA.Domain.Enums;

// BETA — how a contribution folds into the entry value.
// Sum:  EnergySpent, DamageDealt — delta is added to stored value.
// Max:  LargestHit  — stored only when candidate > current value.
// Stat boards are snapshots; they overwrite rather than aggregate.
public enum LeaderboardAggregation
{
    Sum = 0,
    Max = 1,
}
