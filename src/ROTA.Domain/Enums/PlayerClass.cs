namespace ROTA.Domain.Enums;

// Tier 1 (default): Conscript. Tier 2 chosen at L5. Tier 3 chosen at L100.
// Tier 4 (Legendary) auto-advances at L500. Tier 5 (Ascendant) auto-advances at L1000.
public enum PlayerClass
{
    // Tier 1 — default (L1-4)
    Conscript = 0,

    // Tier 2 — chosen at L5 (three paths)
    Ironguard = 1,   // Stamina-focused (raids)
    Arcanist  = 2,   // Energy-focused (quests)
    Sentinel  = 3,   // Balanced

    // Tier 3 — chosen at L100 (specializations)
    Stormguard       = 10,  // Ironguard path
    Bloodguard       = 11,
    Siegebreaker     = 12,
    HighArcanist     = 20,  // Arcanist path
    ShadowArcanist   = 21,
    Runecaller       = 22,
    IroncladSentinel = 30,  // Sentinel path
    Voidwalker       = 31,
    Dawnblade        = 32,

    // Tier 4 — AUTO at L500 (Legendary prefix prepended to current class name)
    LegendaryIronguard        = 101,
    LegendaryArcanist         = 102,
    LegendarySentinel         = 103,
    LegendaryStormguard       = 110,
    LegendaryBloodguard       = 111,
    LegendarySiegebreaker     = 112,
    LegendaryHighArcanist     = 120,
    LegendaryShadowArcanist   = 121,
    LegendaryRunecaller       = 122,
    LegendaryIroncladSentinel = 130,
    LegendaryVoidwalker       = 131,
    LegendaryDawnblade        = 132,

    // Tier 5 — AUTO at L1000 (Ascendant prefix prepended to current class name)
    AscendantIronguard        = 201,
    AscendantArcanist         = 202,
    AscendantSentinel         = 203,
    AscendantStormguard       = 210,
    AscendantBloodguard       = 211,
    AscendantSiegebreaker     = 212,
    AscendantHighArcanist     = 220,
    AscendantShadowArcanist   = 221,
    AscendantRunecaller       = 222,
    AscendantIroncladSentinel = 230,
    AscendantVoidwalker       = 231,
    AscendantDawnblade        = 232,

    // Tier 6 — AUTO at L2000 — ALL paths converge here. Path identity ends.
    Luminary     = 300,

    // Tier 7+ — AUTO, single title per milestone
    Immortal     = 400,   // L5000
    Archon       = 500,   // L7500
    Ancient      = 600,   // L10000
    ElderAncient = 700,   // L15000
    Eternal      = 800,   // L25000
}
