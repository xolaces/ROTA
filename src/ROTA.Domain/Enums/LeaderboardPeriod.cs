namespace ROTA.Domain.Enums;

// BETA — window granularity. Live = Stat snapshot (no calendar bucket). Daily/Weekly/Monthly
// are calendar buckets in UTC; period_key format is deterministic via IPeriodKeyResolver.
public enum LeaderboardPeriod
{
    Live    = 0,
    Daily   = 1,
    Weekly  = 2,
    Monthly = 3,
}
