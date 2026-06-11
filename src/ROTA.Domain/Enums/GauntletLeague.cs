namespace ROTA.Domain.Enums;

/// <summary>
/// The four Gauntlet leagues (T76 — DotD-parity level brackets, owner-locked 2026-06-10:
/// 1–999 / 1000–2499 / 2500–4999 / 5000+; the Ancient bracket was added because late-game
/// power differences are huge). The band a player falls into is locked at first entry for
/// the cycle (System 16). Boundaries are tunable via <c>GauntletConfig.LeagueBounds</c>.
/// </summary>
public enum GauntletLeague
{
    /// <summary>Levels 1–999.</summary>
    Whelpling = 0,

    /// <summary>Levels 1000–2499.</summary>
    Wyrm = 1,

    /// <summary>Levels 2500–4999.</summary>
    Dragon = 2,

    /// <summary>Levels 5000+ — the late-game bracket.</summary>
    Ancient = 3,
}
