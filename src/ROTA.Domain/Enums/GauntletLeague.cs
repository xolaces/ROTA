namespace ROTA.Domain.Enums;

/// <summary>
/// The three Gauntlet leagues, split by convergence tier. The band a player falls into
/// is locked at first entry for the cycle (System 16). Boundaries are tunable via
/// <c>GauntletConfig.LeagueBounds</c>; these defaults mirror the spec.
/// </summary>
public enum GauntletLeague
{
    /// <summary>≤Ascendant — levels 1–1999.</summary>
    Whelpling = 0,

    /// <summary>Luminary–Archon — levels 2000–9999.</summary>
    Wyrm = 1,

    /// <summary>Ancient+ — levels 10000 and above.</summary>
    Dragon = 2,
}
