namespace ROTA.Application.Configuration;

// BETA (System 16 Slice 1) — Gauntlet tuning surface, bound from appsettings
// "GauntletConfig" via IOptions<GauntletConfig>. Defaults mirror the locked spec.
// Validated at startup by GauntletContentProvider (league bounds ordered, contiguous,
// covering the full valid level range from 1; no overlap).
public class GauntletConfig
{
    // The three leagues' level bands, keyed by GauntletLeague name. Bands are stored on
    // each entry at first join; the player's league is locked for the cycle.
    public Dictionary<string, LeagueBound> LeagueBounds { get; set; } = new()
    {
        ["Whelpling"] = new LeagueBound { Min = 1,     Max = 1999 },
        ["Wyrm"]      = new LeagueBound { Min = 2000,  Max = 9999 },
        // Dragon has no upper bound — int.MaxValue is the "no-max" sentinel.
        ["Dragon"]    = new LeagueBound { Min = 10000, Max = NoMaxLevel },
    };

    // Sentinel for "no upper bound" on the top league.
    public const int NoMaxLevel = int.MaxValue;

    // Minimum player level to enter the Gauntlet.
    public int MinEntryLevel { get; set; } = 20;

    // Prizes reach this many ranks (the leaderboard *view* may show fewer).
    public int PrizeRankCount { get; set; } = 500;

    // Leaderboard page size for the public board view.
    public int LeaderboardPageSize { get; set; } = 200;

    // Cadence of the Postgres rank snapshot.
    public int ScoreSnapshotSeconds { get; set; } = 60;

    // Per-hit Strike cost, scaling with hit size (Small/Medium/Large = 1/5/20).
    public StrikeRateBySize StrikeRatePerSize { get; set; } = new();

    // Strikes earned per Gauntlet raid stage defeated.
    public int StrikesPerDefeat { get; set; } = 10;

    // BETA (Slice 2) — gem cost per Strike when buying Strikes with gems (uncapped).
    // Default 1; a tunable balance value (owner to confirm). Total cost = strikes × StrikeGemPrice.
    public int StrikeGemPrice { get; set; } = 1;
}

// Inclusive [Min, Max] level band for a league. Max == GauntletConfig.NoMaxLevel means open-ended.
public class LeagueBound
{
    public int Min { get; set; }
    public int Max { get; set; }
}

// Strike cost per hit size.
public class StrikeRateBySize
{
    public int Small  { get; set; } = 1;
    public int Medium { get; set; } = 5;
    public int Large  { get; set; } = 20;
}
