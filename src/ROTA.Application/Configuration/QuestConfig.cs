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
    // zone ratio multiplies (alongside the difficulty reward multiplier). A battle node's ratio scales
    // with its depth in the chapter (XpZoneRatioBase + ZoneIndex × XpZoneRatioPerZone); a boss node
    // always uses XpBossRatio regardless of zone.
    public double XpZoneRatioBase { get; set; } = 1.2;
    public double XpZoneRatioPerZone { get; set; } = 0.05;
    public double XpBossRatio { get; set; } = 2.0;

    // T55 — co-scaled chapter progression. Replaces the XP-only ChapterXpScalars. The per-node base XP
    // in quests.json ALREADY scales ~26× across chapters 1→6, so stacking an additional large XP scalar
    // on top inflated reward-per-energy to absurd levels (a ch6 attempt granted ~4 levels). The fix:
    //   • EnergyCostMultiplier grows per chapter so ENERGY becomes a meaningful late-game cost (it was
    //     previously flat — only the per-node base + difficulty applied, so late content was nearly free
    //     relative to its huge XP). Energy scaling is the missing half of "XP and energy scale together".
    //   • XpMultiplier stays gentle (the base already carries chapter progression) so XP-per-energy
    //     tightens as you advance — "balanced and intentional at every stage".
    // The lookup index is the CHAPTER (1-based), CAPPED at ChapterScalingCap (16): chapters beyond the
    // cap reuse the cap's entry, so chapters 16→24 share one ratio until a deliberate retune. Designed
    // for the full 24-chapter game though only 6 are built. Values are placeholders for live tuning —
    // they live here (and in appsettings "QuestConfig") so balance changes need no code change.
    public int ChapterScalingCap { get; set; } = 16;

    // Mild intra-chapter energy ramp by zone depth (mirrors the XP zone ratio on the energy side), so a
    // chapter's later zones cost a little more than its first. Keep small — the chapter multiplier does
    // the heavy lifting.
    public double EnergyZoneRampPerZone { get; set; } = 0.04;

    public Dictionary<int, ChapterScalingEntry> ChapterScaling { get; set; } = new()
    {
        [1]  = new() { EnergyCostMultiplier = 1.00, XpMultiplier = 1.00 },
        [2]  = new() { EnergyCostMultiplier = 1.11, XpMultiplier = 1.03 },
        [3]  = new() { EnergyCostMultiplier = 1.24, XpMultiplier = 1.06 },
        [4]  = new() { EnergyCostMultiplier = 1.38, XpMultiplier = 1.10 },
        [5]  = new() { EnergyCostMultiplier = 1.54, XpMultiplier = 1.13 },
        [6]  = new() { EnergyCostMultiplier = 1.71, XpMultiplier = 1.17 },
        [7]  = new() { EnergyCostMultiplier = 1.91, XpMultiplier = 1.20 },
        [8]  = new() { EnergyCostMultiplier = 2.12, XpMultiplier = 1.24 },
        [9]  = new() { EnergyCostMultiplier = 2.37, XpMultiplier = 1.28 },
        [10] = new() { EnergyCostMultiplier = 2.64, XpMultiplier = 1.32 },
        [11] = new() { EnergyCostMultiplier = 2.94, XpMultiplier = 1.36 },
        [12] = new() { EnergyCostMultiplier = 3.28, XpMultiplier = 1.40 },
        [13] = new() { EnergyCostMultiplier = 3.65, XpMultiplier = 1.45 },
        [14] = new() { EnergyCostMultiplier = 4.07, XpMultiplier = 1.49 },
        [15] = new() { EnergyCostMultiplier = 4.53, XpMultiplier = 1.54 },
        [16] = new() { EnergyCostMultiplier = 5.05, XpMultiplier = 1.59 },
    };

    // Resolve the scaling entry for a chapter, clamped to [1, ChapterScalingCap]. Chapters past the cap
    // reuse the cap entry; an unseeded chapter falls back to the nearest lower seeded entry, then to 1×.
    public ChapterScalingEntry GetChapterScaling(int chapter)
    {
        int capped = Math.Clamp(chapter, 1, ChapterScalingCap);
        for (int c = capped; c >= 1; c--)
            if (ChapterScaling.TryGetValue(c, out var entry))
                return entry;
        return new ChapterScalingEntry();
    }

    // BETA — superseded by ChapterScaling (T55). Retained only so older config files / tests that still
    // reference ChapterXpScalars bind without error; the live formula no longer reads it.
    public Dictionary<int, double> ChapterXpScalars { get; set; } = new()
    {
        [1] = 1.0, [2] = 1.6, [3] = 2.6, [4] = 4.2, [5] = 7.0, [6] = 11.0,
    };
}

// T55 — per-chapter co-scaling of energy cost and XP reward. Bound from appsettings
// "QuestConfig:ChapterScaling:<chapter>:{EnergyCostMultiplier,XpMultiplier}". Plain get/set props so
// the configuration binder can populate it (records with positional ctors don't bind cleanly).
public class ChapterScalingEntry
{
    public double EnergyCostMultiplier { get; set; } = 1.0;
    public double XpMultiplier { get; set; } = 1.0;
}
