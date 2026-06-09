using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

/// <summary>
/// Static content definition for one achievement (TICKET 46). Loaded from
/// <c>content/achievements.json</c> by <c>IAchievementDefinitionProvider</c>; validated at startup.
/// Fully data-driven: a category, the tracked <see cref="Metric"/>, a point value, a
/// <see cref="Threshold"/> and an optional <see cref="NextId"/> that chains tiers (e.g. Slayer →
/// Raid Veteran). Mirrors <c>AncientDefinition</c> (content-shaped, re-tunable without code).
/// </summary>
public class AchievementDefinition
{
    public string Id { get; set; } = string.Empty;
    public AchievementCategory Category { get; set; }
    public AchievementMetric Metric { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Achievement Points awarded on completion. Must be &gt; 0 (startup-validated).</summary>
    public int Points { get; set; }

    /// <summary>The metric counter value that completes this achievement. Must be &gt; 0 (startup-validated).</summary>
    public long Threshold { get; set; }

    /// <summary>Reserved — repeatable achievements are PHASE-2. Currently always false.</summary>
    public bool Repeatable { get; set; } = false;

    /// <summary>
    /// The next tier in this chain, or null at the end. Must resolve to another achievement with the
    /// SAME metric and a STRICTLY HIGHER threshold (startup-validated; no cycles). Tiers award
    /// independently — crossing a higher threshold awards both the lower and higher tier.
    /// </summary>
    public string? NextId { get; set; }

    /// <summary>
    /// For <see cref="AchievementCategory.Collector"/> achievements: the item Type/Tag whose distinct
    /// owned count this watches (e.g. "Sigil"). REQUIRED when Category == Collector (startup-validated).
    /// </summary>
    public string? CollectorKey { get; set; }

    public string? IconKey { get; set; }
}
