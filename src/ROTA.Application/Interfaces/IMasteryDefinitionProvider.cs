using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Reads <c>content/masteries.json</c> once at startup and exposes the four Ancient definitions
/// (System 22 Phase A). Implementations throw <see cref="InvalidOperationException"/> at construction
/// on invalid content (missing/duplicate Ancient, bad magnitude table, bad tier checklist).
/// </summary>
public interface IMasteryDefinitionProvider
{
    /// <summary>All four Ancient definitions.</summary>
    IReadOnlyList<AncientDefinition> GetAll();

    /// <summary>The definition for one Ancient, or null if absent.</summary>
    AncientDefinition? Get(MasteryAncient ancient);

    /// <summary>The always-on global modifier percent for an Ancient at a given level (1..5).</summary>
    double GlobalPercent(MasteryAncient ancient, int level);

    /// <summary>The tier-advance checklist that advances an Ancient FROM the given level (1..4), or null.</summary>
    MasteryTierChallenge? GetTierChallenge(MasteryAncient ancient, int fromLevel);
}
