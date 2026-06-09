namespace ROTA.Application.Configuration;

// Quest node depletion (System 20). Each node starts at NodeStartProgress and depletes per
// attempt until it hits 0 and clears, unlocking the next node. Boss nodes deplete slower to
// reflect their weight. Config-driven (appsettings "QuestConfig") — no magic numbers in the service.
public class QuestConfig
{
    public double NodeStartProgress { get; set; } = 100.0;
    public double BattleDepletionPerAttempt { get; set; } = 5.0;   // 100 / 5  = 20 attempts to clear
    public double BossDepletionPerAttempt { get; set; } = 2.5;     // 100 / 2.5 = 40 attempts to clear

    // Discernment-scaled chance drops (System 20 Slice 2). Each point of DiscernmentInvestment
    // raises a chance-drop's effective rate by DiscernmentDropMultiplier (relative to its base),
    // clamped at MaxDropChance. Guaranteed drops are unaffected. Example: base 0.05, Discernment 30,
    // k 0.03 → 0.05 × (1 + 30×0.03) = 0.05 × 1.9 = 0.095.
    public double DiscernmentDropMultiplier { get; set; } = 0.03;
    public double MaxDropChance { get; set; } = 0.95;

    // T44 — zone-indexed XP formula. ExperienceReward in quests.json is the per-node BASE that the
    // zone ratio and the per-chapter scalar multiply (alongside the difficulty reward multiplier).
    // A battle node's ratio scales with its depth in the chapter (XpZoneRatioBase + ZoneIndex ×
    // XpZoneRatioPerZone); a boss node always uses XpBossRatio regardless of zone. ChapterXpScalars
    // then make late chapters award meaningfully more (early LOW, late HIGH).
    public double XpZoneRatioBase { get; set; } = 1.2;
    public double XpZoneRatioPerZone { get; set; } = 0.05;
    public double XpBossRatio { get; set; } = 2.0;
    public Dictionary<int, double> ChapterXpScalars { get; set; } = new()
    {
        [1] = 1.0,
        [2] = 1.6,
        [3] = 2.6,
        [4] = 4.2,
        [5] = 7.0,
        [6] = 11.0,
    };
}
