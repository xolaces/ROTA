namespace ROTA.Domain.Enums;

/// <summary>
/// The completed→lootable→looted lifecycle of an active raid. NOTE: raid rewards are FULLY granted on
/// the killing hit (inside DistributeKillRewardsAsync) — there is NO unclaimed-reward state. "Loot"
/// here is a DISMISS / REMOVE-FROM-ALL-INDEXES action, not a reward claim: it lets the summoner clear
/// a defeated raid from the lists once they've seen the result screen.
/// </summary>
public enum RaidLifecycleState
{
    /// <summary>Alive and being fought (or freshly summoned). The only state that lists.</summary>
    Active   = 0,

    /// <summary>Defeated — rewards already granted on the killing hit; awaiting summoner dismissal.</summary>
    Lootable = 1,

    /// <summary>Dismissed by the summoner — removed from all indexes (history/participants stay intact).</summary>
    Looted   = 2,
}
