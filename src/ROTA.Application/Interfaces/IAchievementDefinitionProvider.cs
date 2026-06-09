using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Reads <c>content/achievements.json</c> once at startup and exposes the achievement roster
/// (TICKET 46). Implementations throw <see cref="InvalidOperationException"/> at construction on
/// invalid content (duplicate id, unknown category/metric, non-positive points/threshold,
/// dangling/cyclic NextId, non-increasing threshold along a chain, missing CollectorKey on a
/// Collector achievement). Mirrors <see cref="IMasteryDefinitionProvider"/>.
/// </summary>
public interface IAchievementDefinitionProvider
{
    /// <summary>All achievement definitions.</summary>
    IReadOnlyList<AchievementDefinition> GetAll();

    /// <summary>One achievement by id, or null if absent.</summary>
    AchievementDefinition? GetById(string id);

    /// <summary>All achievements that watch the given metric.</summary>
    IReadOnlyList<AchievementDefinition> GetByMetric(AchievementMetric metric);
}
