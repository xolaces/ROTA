namespace ROTA.Domain.Enums;

/// <summary>
/// Deterministic activity counters that feed the per-Ancient mastery challenge checklists
/// (System 22, Phase A). Each value is a running per-player tally incremented best-effort at the
/// activity's chokepoint (Slice 4). Tier checklists in <c>content/masteries.json</c> select which
/// counters + thresholds advance each Ancient 1→5. Persisted as <c>int</c> — append, never renumber.
/// </summary>
public enum MasteryActivityType
{
    /// <summary>A raid hit landed (count of hits, any size).</summary>
    RaidHit = 0,

    /// <summary>Damage dealt to raids (sum of damageFinal).</summary>
    RaidDamageDealt = 1,

    /// <summary>A raid boss defeated (the killer).</summary>
    RaidKill = 2,

    /// <summary>A quest node cleared (depletion reached 0).</summary>
    QuestNodeCleared = 3,

    /// <summary>A quest chapter boss cleared (chapter reset).</summary>
    QuestBossCleared = 4,

    /// <summary>Damage contributed to guild raids (sum).</summary>
    GuildRaidContribution = 5,

    /// <summary>A Gauntlet placement settled inside the prize ranks.</summary>
    GauntletRankEarned = 6,

    /// <summary>A character level gained.</summary>
    LevelGained = 7,

    /// <summary>Energy spent.</summary>
    EnergySpent = 8,

    /// <summary>Stamina (or guild stamina) spent.</summary>
    StaminaSpent = 9,

    /// <summary>Gold earned.</summary>
    GoldEarned = 10,
}
