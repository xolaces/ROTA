namespace ROTA.Application.Configuration;

// System 22 Phase A (Masteries) — scalar tuning surface, bound from appsettings "MasteryConfig"
// via IOptions<MasteryConfig>. Per-Ancient per-level magnitudes live in content/masteries.json;
// this holds only the cross-cutting scalar dials. Defaults mirror the locked spec.
public class MasteryConfig
{
    // Pledged Ancient's bonus = global × this (the "pledging ≈ doubles" rule). TUNE.
    public double PledgeMultiplier { get; set; } = 2.0;

    // Hard cap on Bulwark's guild-raid damage %, pledged or not — a direct combat %, the
    // deliberate exception (the other three lanes are mostly horizontal, so they can be generous).
    public double BulwarkMaxGuildDamagePercent { get; set; } = 1.0;

    // Flat gem cost of a paid (weekly) re-spec. TUNE. Mirrors GauntletConfig.StrikeGemPrice.
    public int RespecGemCost { get; set; } = 150;

    // Formula B optional weakest-pillar floor (+2×min(levels)). Default OFF (the worked examples
    // 10/14/56 exclude it).
    public bool IncludeWeakestPillarFloor { get; set; } = false;

    // Capped breadth micro-bonus added to the Hoard drop/gold lane once all four Ancients ≥ 3.
    // Default 0.0 (off until the owner sizes it); hard-capped at BreadthMicroBonusMaxPercent.
    public double BreadthMicroBonusPercent    { get; set; } = 0.0;
    public double BreadthMicroBonusMaxPercent { get; set; } = 2.0;

    // Whether Discernment "sigil find" scales the GUARANTEED first-clear sigil drop. Locked false —
    // the first-per-difficulty sigil is always a guaranteed drop and is never scaled.
    public bool SigilFindAppliesToFirstClear { get; set; } = false;
}
