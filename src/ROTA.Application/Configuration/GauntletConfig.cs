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

    // ── T54 — formula-driven ladder extension + late-game curve ──────────────────────────────────
    // When MaxLadderStage > the JSON stage count, the content provider regenerates the ladder as
    // MaxLadderStage stages whose HP/rewards come from the curve below (names/art kept from
    // gauntlet_raids.json for the early named stages). This reaches a genuine late-game ceiling
    // (level 250) without authoring hundreds of JSON rows. 0 = OFF: the ladder is exactly the JSON
    // stages (the default, so small-fixture unit tests are unaffected). appsettings sets it to 250.
    //
    // CURVE: H(n) = StageHpBase × StageHpGrowth^(n-1). The Gauntlet is a POWER TREADMILL — a hit deals
    // ~ (battalion power) damage and a defeat refunds StrikesPerDefeat strikes, so a stage breaks even
    // when H(n) ≈ StrikesPerDefeat × power. With the defaults (base 5000, growth 1.0493, StrikesPerDefeat
    // 10): H(1)=5000 and H(250)≈8.0e8 ⇒ break-even at ~80,000,000 power — the presumed ~1-year endgame
    // base battalion power. A player's natural Gauntlet level ≈ where their CURRENT power hits break-even,
    // so the climb tracks the year-long power curve; buying strikes with gems and the (gem-funded)
    // double-power buff push a handful of stages past that natural wall. As power creeps, raise the growth
    // or apply a global debuff (owner's lever) — every value here is appsettings-overridable.
    // See GauntletStageCurve + GauntletCurveTests for the simulated timeline ("test run").
    public int    MaxLadderStage            { get; set; } = 0;
    public long   StageHpBase               { get; set; } = 5000;
    public double StageHpGrowth             { get; set; } = 1.0493;
    public long   StageBaseGoldReward       { get; set; } = 200;
    public int    StageBaseExperienceReward { get; set; } = 150;
    public double StageRewardGrowth         { get; set; } = 1.04;
}

// T54 — the pure Gauntlet ladder curve. Shared by the content provider (stage generation) and the
// curve test (the "test run" the owner asked for) so both compute identical numbers.
public static class GauntletStageCurve
{
    public static long Hp(int stage, GauntletConfig c)
        => (long)Math.Round(c.StageHpBase * Math.Pow(c.StageHpGrowth, stage - 1), MidpointRounding.AwayFromZero);

    public static long Gold(int stage, GauntletConfig c)
        => (long)Math.Round(c.StageBaseGoldReward * Math.Pow(c.StageRewardGrowth, stage - 1), MidpointRounding.AwayFromZero);

    public static int Xp(int stage, GauntletConfig c)
        => (int)Math.Round(c.StageBaseExperienceReward * Math.Pow(c.StageRewardGrowth, stage - 1), MidpointRounding.AwayFromZero);

    // The battalion power at which a stage breaks even: cost-to-defeat (Hp/power strikes) equals the
    // StrikesPerDefeat refund. A player whose power ≥ this clears the stage net-positive on strikes.
    public static double BreakEvenPower(int stage, GauntletConfig c)
        => (double)Hp(stage, c) / Math.Max(1, c.StrikesPerDefeat);
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
