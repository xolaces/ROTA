using ROTA.Domain.Enums;

namespace ROTA.Application.Configuration;

// TICKET 46 (Achievements) — scalar tuning surface, bound from appsettings "AchievementConfig" via
// IOptions<AchievementConfig>. Per-achievement point/threshold values live in
// content/achievements.json; this holds cross-cutting scalar dials. Mirrors MasteryConfig's role so
// tuning needs no content-schema change.
public class AchievementConfig
{
    // System 25 — the rarity ladder applied to EVERY quest zone's rerun achievements. The
    // AchievementDefinitionProvider expands this across the distinct (chapter, zone) pairs from the quest
    // roster at boot (one 6-tier chain per zone, deterministic ids), so adding chapters/zones to
    // quests.json grows the achievement roster automatically — no hand-authored rows. Thresholds must be
    // strictly increasing along the ladder (boot-validated). Owner-locked curve 2026-06-16.
    public List<ZoneRerunTier> ZoneRerunLadder { get; set; } = new()
    {
        new() { Rarity = ItemRarity.Grey,   Threshold = 10,  Points = 5   },
        new() { Rarity = ItemRarity.White,  Threshold = 25,  Points = 10  },
        new() { Rarity = ItemRarity.Green,  Threshold = 50,  Points = 20  },
        new() { Rarity = ItemRarity.Blue,   Threshold = 100, Points = 40  },
        new() { Rarity = ItemRarity.Purple, Threshold = 250, Points = 75  },
        new() { Rarity = ItemRarity.Orange, Threshold = 500, Points = 150 },
    };
}

// One rung of the zone-rerun ladder: a rarity tier completed at Threshold reruns for Points AP.
public class ZoneRerunTier
{
    public ItemRarity Rarity { get; set; }
    public long Threshold { get; set; }
    public int Points { get; set; }
}
