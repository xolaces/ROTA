namespace ROTA.Domain.Enums;

/// <summary>
/// The completed→lootable→looted lifecycle of an active raid. T57 NOTE: only XP + gold are granted on
/// the killing hit; everything else (gems, stat-points, inventory items, collection drops) is rolled at
/// kill, stored pending on each participant row, and GRANTED PER-PARTICIPANT when that player presses
/// Loot. The raid stays Lootable while any participant still has an unclaimed reward; it flips to Looted
/// once the last participant has claimed (or the summoner dismisses it), at which point it drops out of
/// every index. Looted does NOT soft-delete — history/participants stay intact.
/// </summary>
public enum RaidLifecycleState
{
    /// <summary>Alive and being fought (or freshly summoned). The only state that lists.</summary>
    Active   = 0,

    /// <summary>Defeated — XP/gold already granted on the killing hit; per-participant deferred rewards
    /// awaiting their Loot claim. Listed in the lootable indexes while any reward is unclaimed.</summary>
    Lootable = 1,

    /// <summary>Fully claimed or dismissed — removed from all indexes (history/participants stay intact).</summary>
    Looted   = 2,
}
