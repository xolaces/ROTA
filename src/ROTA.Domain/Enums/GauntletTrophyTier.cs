namespace ROTA.Domain.Enums;

/// <summary>
/// Gauntlet trophy tiers (System 16). Trophies are permanent and stack highest-only —
/// owning several applies only the best <c>LegionPowerBonusFraction</c>.
/// </summary>
public enum GauntletTrophyTier
{
    /// <summary>Rank 1 — +25% legion power.</summary>
    Aureate = 0,

    /// <summary>Rank 10 — +10% legion power.</summary>
    Argent = 1,

    /// <summary>Rank 500 — +5% legion power.</summary>
    Bronzed = 2,
}
