using ROTA.Domain.Enums;

namespace ROTA.Application.Configuration;

// TICKET 46 (Achievements) — scalar tuning surface, bound from appsettings "AchievementConfig" via
// IOptions<AchievementConfig>. Per-achievement point/threshold values live in
// content/achievements.json; this holds cross-cutting scalar dials. Mirrors MasteryConfig's role so
// tuning needs no content-schema change.
public class AchievementConfig
{
    // the rarity ladder applied to EVERY quest zone's rerun achievements. The
    // AchievementDefinitionProvider expands this across the distinct (chapter, zone) pairs from the quest
    // roster at boot (one 6-tier chain per zone, deterministic ids), so adding chapters/zones to
    // quests.json grows the achievement roster automatically — no hand-authored rows. Thresholds must be
    // strictly increasing along the ladder (boot-validated). Owner-locked curve 2026-06-16.
    public List<ZoneRerunTier> ZoneRerunLadder { get; set; } = new()
    {
        new() { Rarity = ItemRarity.Grey,   Threshold = 10,   Points = 5    },
        new() { Rarity = ItemRarity.White,  Threshold = 25,   Points = 10   },
        new() { Rarity = ItemRarity.Green,  Threshold = 50,   Points = 20   },
        new() { Rarity = ItemRarity.Blue,   Threshold = 100,  Points = 40   },
        new() { Rarity = ItemRarity.Purple, Threshold = 250,  Points = 75   },
        new() { Rarity = ItemRarity.Orange, Threshold = 500,  Points = 150  },
        new() { Rarity = ItemRarity.Orange, Threshold = 1000, Points = 300  },
        new() { Rarity = ItemRarity.Orange, Threshold = 2500, Points = 700  },
        new() { Rarity = ItemRarity.Orange, Threshold = 5000, Points = 1500 },
    };

    // Owner 2026-09-03 — the same nine-rung ladder, applied PER RAID. Before this, RaidCompletions was
    // one GLOBAL counter with two authored rungs (10 and 100), so a raid roster of any size shared a
    // ladder that finished in the first afternoon. This expands across the raid roster at boot exactly
    // as ZoneRerunLadder expands across quest zones, and clears are counted per raid definition.
    //
    // 5,000 is the deliberate chase ceiling, not an expected completion: maxing it on all 25 raids is
    // 125,000 clears against a stated ~50,000-raid endgame, so the top rung is meant to stay unfinished
    // on most of the roster. Thresholds must be strictly increasing (boot-validated).
    public List<ZoneRerunTier> RaidClearLadder { get; set; } = new()
    {
        new() { Rarity = ItemRarity.Grey,   Threshold = 10,   Points = 5    },
        new() { Rarity = ItemRarity.White,  Threshold = 25,   Points = 10   },
        new() { Rarity = ItemRarity.Green,  Threshold = 50,   Points = 20   },
        new() { Rarity = ItemRarity.Blue,   Threshold = 100,  Points = 40   },
        new() { Rarity = ItemRarity.Purple, Threshold = 250,  Points = 75   },
        new() { Rarity = ItemRarity.Orange, Threshold = 500,  Points = 150  },
        new() { Rarity = ItemRarity.Orange, Threshold = 1000, Points = 300  },
        new() { Rarity = ItemRarity.Orange, Threshold = 2500, Points = 700  },
        new() { Rarity = ItemRarity.Orange, Threshold = 5000, Points = 1500 },
    };
}

// One rung of a clear ladder: a rarity tier completed at Threshold clears for Points AP.
//
// Rarity is PRESENTATION ONLY (it picks the icon), and it deliberately repeats across the top rungs —
// ItemRarity stops at Orange by design and must never grow, so a nine-rung ladder cannot have nine
// distinct rarities. Ladder identity therefore comes from Threshold, never from Rarity.
public class ZoneRerunTier
{
    public ItemRarity Rarity { get; set; }
    public long Threshold { get; set; }
    public int Points { get; set; }
}
