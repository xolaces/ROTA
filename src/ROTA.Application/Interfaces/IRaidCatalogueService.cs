using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Read-only projection of raid CONTENT for the summon screen: what a boss is, how much health it
/// has, and what its loot table pays out per contribution bracket.
///
/// Separate from IRaidService on purpose. Nothing here touches the database, a player, or a lock —
/// it reads content that is already loaded and boot-validated. RaidService carries two dozen
/// dependencies precisely because it does the opposite of that.
/// </summary>
public interface IRaidCatalogueService
{
    /// <summary>Every raid definition, as the summon list needs it. No player state.</summary>
    IReadOnlyList<RaidPreviewResponse> GetCatalogue();

    /// <summary>One raid, or null if the id is unknown.</summary>
    RaidPreviewResponse? GetPreview(string raidDefinitionId);

    /// <summary>
    /// The contribution brackets for one raid at one difficulty. Null when the raid is unknown or
    /// its loot table does not define that difficulty — the caller renders those as 404, since
    /// "this boss has no Nightmare tier" and "this boss drops nothing" are different answers.
    /// </summary>
    RaidLootPreviewResponse? GetLootPreview(string raidDefinitionId, string difficulty);
}
