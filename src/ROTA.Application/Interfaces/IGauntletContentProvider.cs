using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 1) — singleton content provider for Gauntlet prizes, trophies,
// and the raid ladder. Mirrors IMagicDefinitionProvider: JSON files read once at
// construction; validation throws at startup on invalid data.
public interface IGauntletContentProvider
{
    // Prize table (single object with contiguous bands covering 1..PrizeRankCount).
    GauntletPrizeTable GetPrizeTable();

    // The prize band containing the given rank, or null if the rank is unawarded.
    GauntletPrizeBand? GetBandForRank(int rank);

    // All trophy definitions.
    IReadOnlyList<GauntletTrophyDefinition> GetAllTrophies();

    // A trophy definition by id, or null.
    GauntletTrophyDefinition? GetTrophyById(string id);

    // All Gauntlet raid stages, ordered ascending by LadderStage.
    IReadOnlyList<GauntletRaidDefinition> GetGauntletRaids();

    // A Gauntlet raid stage by its 1-based ladder position, or null.
    GauntletRaidDefinition? GetGauntletRaidByStage(int ladderStage);

    // Resolve the league for a given player level using the configured bounds.
    GauntletLeague ResolveLeague(int level);
}
