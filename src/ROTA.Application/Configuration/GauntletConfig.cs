namespace ROTA.Application.Configuration;

// BETA (System 16 Slice 1) — Gauntlet tuning surface, bound from appsettings
// "GauntletConfig" via IOptions<GauntletConfig>. Defaults mirror the locked spec.
// Validated at startup by GauntletContentProvider (league bounds ordered, contiguous,
// covering the full valid level range from 1; no overlap).
public class GauntletConfig
{
    // The four leagues' level bands, keyed by GauntletLeague name (T76 — DotD-parity brackets,
    // owner-locked: 1–999 / 1000–2499 / 2500–4999 / 5000+). Bands are stored on each entry at
    // first join; the player's league is locked for the cycle.
    public Dictionary<string, LeagueBound> LeagueBounds { get; set; } = new()
    {
        ["Whelpling"] = new LeagueBound { Min = 1,    Max = 999 },
        ["Wyrm"]      = new LeagueBound { Min = 1000, Max = 2499 },
        ["Dragon"]    = new LeagueBound { Min = 2500, Max = 4999 },
        // Ancient has no upper bound — int.MaxValue is the "no-max" sentinel.
        ["Ancient"]   = new LeagueBound { Min = 5000, Max = NoMaxLevel },
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

    // How often the settlement sweeper looks for an event that has reached EndsAt. An event runs for
    // days, so settling within a minute of the clock expiring is ample.
    public int SettlementSweepSeconds { get; set; } = 60;

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

    // ── T76 — late-ladder brutality ramp (owner-locked, mirrors DotD's feel: "not brutal until
    // ~230, near-doubling per stage by ~250"). Past LateRampStartStage the per-stage growth factor
    // climbs LINEARLY from StageHpGrowth at the ramp start to LateRampFinalGrowth at MaxLadderStage.
    // 0 = OFF (default, so unit fixtures and the documented base curve stay untouched);
    // appsettings turns it on at stage 200 with a final growth of 2.0 (HP doubles per stage at 250).
    public int    LateRampStartStage  { get; set; } = 0;
    public double LateRampFinalGrowth { get; set; } = 2.0;
}

// T54 — the pure Gauntlet ladder curve. Shared by the content provider (stage generation) and the
// curve test (the "test run" the owner asked for) so both compute identical numbers.
public static class GauntletStageCurve
{
    public static long Hp(int stage, GauntletConfig c)
    {
        // Base regime: pure exponential. Applies to the whole ladder when the ramp is off,
        // and to every stage at or below the ramp start when it is on.
        bool rampOn = c.LateRampStartStage > 0 && c.MaxLadderStage > c.LateRampStartStage;
        if (!rampOn || stage <= c.LateRampStartStage)
            return (long)Math.Round(c.StageHpBase * Math.Pow(c.StageHpGrowth, stage - 1), MidpointRounding.AwayFromZero);

        // T76 late ramp: growth interpolates linearly from StageHpGrowth (at the ramp start) to
        // LateRampFinalGrowth (at MaxLadderStage). HP accumulates multiplicatively per stage, so
        // the curve stays continuous at the ramp boundary and ends near-doubling per stage.
        double hp = c.StageHpBase * Math.Pow(c.StageHpGrowth, c.LateRampStartStage - 1);
        int rampSpan = c.MaxLadderStage - c.LateRampStartStage;
        int top = Math.Min(stage, c.MaxLadderStage);
        for (int n = c.LateRampStartStage + 1; n <= top; n++)
        {
            double t = (double)(n - c.LateRampStartStage) / rampSpan;
            hp *= c.StageHpGrowth + (c.LateRampFinalGrowth - c.StageHpGrowth) * t;
        }
        // Stages past MaxLadderStage (shouldn't exist) keep the final growth factor.
        for (int n = c.MaxLadderStage + 1; n <= stage; n++)
            hp *= c.LateRampFinalGrowth;
        return (long)Math.Round(hp, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Whether the top of the configured ladder still fits in a <see cref="long"/>. Boot-checked,
    /// because the overflow is silent: StageHp accumulates in <c>double</c> and casts to
    /// <c>long</c>, and a double outside long's range has no defined conversion in an unchecked
    /// context — the result is a garbage MaxHp, not an exception.
    ///
    /// The ramp interpolates across LateRampStartStage..MaxLadderStage, so widening that span adds
    /// near-doubling stages and multiplies the top HP rather than stretching the same curve further.
    /// Shipped 250 tops out at 6.22e16 (148x under the ceiling); 300 tops out at 3.89e25, over it.
    ///
    /// NOTE the curve already exceeds 2^53 from about stage 248, so the top few stages are not
    /// exactly representable as doubles — HP(250) is precise only to the nearest 8. That is
    /// 1.3e-14% of the value and irrelevant to balance, which is why the check is against long's
    /// range and not against exact integer precision.
    /// </summary>
    public static bool StageHpIsRepresentable(GauntletConfig c)
    {
        // MaxLadderStage <= 0 means "use the ladder as authored in JSON" — there is no generated top
        // stage to check, so there is nothing here that can overflow.
        if (c.MaxLadderStage <= 0) return true;

        double hp = StageHpAsDouble(c, c.MaxLadderStage);
        return double.IsFinite(hp) && hp > 0 && hp <= long.MaxValue;
    }

    // The same accumulation as StageHp, stopping before the narrowing cast so the check can see the
    // value that would overflow rather than the garbage it would turn into.
    private static double StageHpAsDouble(GauntletConfig c, int stage)
    {
        if (stage < 1) return 0;
        bool rampOn = c.LateRampStartStage > 0 && c.MaxLadderStage > c.LateRampStartStage;
        if (!rampOn || stage <= c.LateRampStartStage)
            return c.StageHpBase * Math.Pow(c.StageHpGrowth, stage - 1);

        double hp = c.StageHpBase * Math.Pow(c.StageHpGrowth, c.LateRampStartStage - 1);
        int rampSpan = c.MaxLadderStage - c.LateRampStartStage;
        int top = Math.Min(stage, c.MaxLadderStage);
        for (int n = c.LateRampStartStage + 1; n <= top; n++)
        {
            double t = (double)(n - c.LateRampStartStage) / rampSpan;
            hp *= c.StageHpGrowth + (c.LateRampFinalGrowth - c.StageHpGrowth) * t;
        }
        for (int n = c.MaxLadderStage + 1; n <= stage; n++)
            hp *= c.LateRampFinalGrowth;
        return hp;
    }

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
