namespace ROTA.Domain.Enums;

/// <summary>
/// The tracked counter an achievement watches (TICKET 46). Each metric is a running per-player tally
/// (or an absolute recount, for the EquipmentPiecesOwned / CollectorItemCount metrics) compared
/// against the achievement's threshold. Mirrors <see cref="MasteryActivityType"/>. Persisted as
/// <c>int</c> inside the AchievementProgress row — append, never renumber.
/// </summary>
public enum AchievementMetric
{
    /// <summary>A raid boss defeated (the killer). Incremented in the raid kill path.</summary>
    RaidCompletions = 0,

    /// <summary>A quest node cleared (depletion reached 0).</summary>
    QuestNodesCleared = 1,

    /// <summary>A quest zone/chapter boss cleared.</summary>
    QuestBossesCleared = 2,

    /// <summary>Total equipment pieces owned (RECOUNTED absolute on gear grant — never delta).</summary>
    EquipmentPiecesOwned = 3,

    /// <summary>Distinct UTC calendar days the player has logged in.</summary>
    DaysPlayed = 4,

    /// <summary>Distinct items owned matching a CollectorKey (RECOUNTED absolute on item grant).</summary>
    CollectorItemCount = 5,
}
