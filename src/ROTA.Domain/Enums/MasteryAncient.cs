namespace ROTA.Domain.Enums;

/// <summary>
/// The four pledgeable Ancients of the Masteries system (System 22, Phase A). Each is leveled
/// 1→5 and grants an always-on global modifier plus an amplified bonus while pledged. Each owns a
/// different, orthogonal pillar so no single sim can rank them against each other.
/// Persisted as <c>int</c> — append new Ancients at the end, never renumber.
/// </summary>
public enum MasteryAncient
{
    /// <summary>The Wrathfire — rage/war. +% Legion power (never the Gauntlet).</summary>
    Wrath = 0,

    /// <summary>The Mountain — guild/defence. +% guild-raid damage (hard-capped; guild raids only).</summary>
    Bulwark = 1,

    /// <summary>The Greed — plunder. +% global drop rate / gold.</summary>
    Hoard = 2,

    /// <summary>The Veiled Eye — sight/discovery. +% drop quality (rarity-upgrade) + sigil find.</summary>
    Discernment = 3,
}
