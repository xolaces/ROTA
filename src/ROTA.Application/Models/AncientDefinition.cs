using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

/// <summary>
/// Static content definition for one Ancient (System 22 Phase A). Loaded from
/// <c>content/masteries.json</c> by <c>IMasteryDefinitionProvider</c>; validated at startup.
/// Per-level magnitudes live here (content-shaped, re-tunable without code); cross-cutting scalar
/// dials live in <c>MasteryConfig</c>.
/// </summary>
public class AncientDefinition
{
    public MasteryAncient Ancient { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Theme       { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The always-on global modifier percent at each level. Index 0 = L1 … index 4 = L5
    /// (e.g. <c>2.5</c> = +2.5%). Must have 5 entries, non-negative, non-decreasing. The pledged
    /// Ancient applies <c>global × MasteryConfig.PledgeMultiplier</c> (Bulwark clamps to its cap).
    /// </summary>
    public double[] GlobalPercentByLevel { get; set; } = [];

    /// <summary>
    /// The four tier-advance checklists (T1→2, T2→3, T3→4, T4→5), one per <c>FromLevel</c> 1..4.
    /// </summary>
    public List<MasteryTierChallenge> TierChallenges { get; set; } = new();

    public string IconKey { get; set; } = string.Empty;
}

/// <summary>One tier-advance checklist: advance from <see cref="FromLevel"/> to the next level when
/// EVERY item in <see cref="Checklist"/> is met.</summary>
public class MasteryTierChallenge
{
    /// <summary>The level this checklist advances FROM (1..4).</summary>
    public int FromLevel { get; set; }
    public List<MasteryChallengeItem> Checklist { get; set; } = new();
}

/// <summary>One checklist requirement: reach <see cref="Threshold"/> of <see cref="ActivityType"/>.</summary>
public class MasteryChallengeItem
{
    public MasteryActivityType ActivityType { get; set; }
    public long Threshold { get; set; }
}
