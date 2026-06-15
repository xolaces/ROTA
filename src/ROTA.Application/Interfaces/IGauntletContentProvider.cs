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

    // T76 — kind-aware band lookup. Neck = the standard bands; Ring = the ring set when authored,
    // else the Neck bands with MagicId stripped (rings never grant rank magics).
    GauntletPrizeBand? GetBandForRank(int rank, GauntletEventKind kind);

    // T76 — the full kind-aware band list (same Ring fallback rule as GetBandForRank), ordered by
    // RankFrom ascending. Feeds the event page's prize preview table.
    IReadOnlyList<GauntletPrizeBand> GetBands(GauntletEventKind kind);

    IReadOnlyList<GauntletTrophyDefinition> GetAllTrophies();

    // A trophy definition by id, or null.
    GauntletTrophyDefinition? GetTrophyById(string id);

    // All Gauntlet raid stages, ordered ascending by LadderStage.
    IReadOnlyList<GauntletRaidDefinition> GetGauntletRaids();

    // A Gauntlet raid stage by its 1-based ladder position, or null.
    GauntletRaidDefinition? GetGauntletRaidByStage(int ladderStage);

    // T76 — a Gauntlet raid stage by its raid-definition id (the combat kill hook's reverse
    // lookup: ActiveRaid.RaidDefinitionId → ladder stage), or null for non-Gauntlet definitions.
    GauntletRaidDefinition? GetGauntletRaidByDefinitionId(string raidDefinitionId);

    // Resolve the league for a given player level using the configured bounds.
    GauntletLeague ResolveLeague(int level);
}
