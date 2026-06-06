using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

// BETA (System 16 Slice 1) — a Gauntlet trophy definition, loaded from
// content/gauntlet_trophies.json. Trophies are permanent and stack highest-only:
// trophyMult = 1.0 + Max(ownedTrophies.Select(t => t.LegionPowerBonusFraction)).
// Combat applies this in Slice 4; this slice is content + validation only.
public class GauntletTrophyDefinition
{
    public string             Id   { get; set; } = string.Empty;
    public string             Name { get; set; } = string.Empty;
    public GauntletTrophyTier Tier { get; set; }

    // Fraction added to legion power (e.g. 0.25 = +25%). Must be > 0.
    public double LegionPowerBonusFraction { get; set; }
}
