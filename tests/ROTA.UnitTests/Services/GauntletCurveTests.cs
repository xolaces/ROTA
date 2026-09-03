using FluentAssertions;
using ROTA.Application.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace ROTA.UnitTests.Services;

// T54 — the Gauntlet ladder curve "test run" the owner asked for. The Gauntlet is a POWER TREADMILL:
// a hit deals ~ (battalion power) damage and defeating a stage refunds StrikesPerDefeat strikes, so
// stage n breaks even at power H(n)/StrikesPerDefeat. The presumed endgame BASE battalion power is
// ~80,000,000 (≈1 year of progress), and the curve is tuned so stage 250 breaks even right there —
// making 250 the natural late-game frontier. A player's Gauntlet level ≈ where their CURRENT power hits
// break-even, so the climb tracks the year-long power curve; gems (buy strikes) + the gem-funded
// double-power buff push a handful of stages past that natural wall.
//
// These tests (a) assert the curve is sane and (b) PRINT a stage/break-even/gem-cost table so the owner
// can eyeball and tune it without touching code (all curve params are appsettings-overridable).
public class GauntletCurveTests
{
    private readonly ITestOutputHelper _out;
    public GauntletCurveTests(ITestOutputHelper output) => _out = output;

    // MIRRORS appsettings.json. This fixture used to set only MaxLadderStage, on the belief that
    // "defaults already match appsettings" — but T76's late ramp is opt-in too (LateRampStartStage
    // defaults to 0 = off), so every test below exercised the PURE EXPONENTIAL curve while production
    // ships the ramped one. The gap is not small: H(250) is 8.0e8 unramped and 6.2e16 shipped.
    //
    // That is the same failure that let XpExponent ship at 0.8 while defaulting to 0.7 — a green suite
    // characterising a curve nobody runs. Any opt-in knob added to GauntletConfig must be added here.
    private static GauntletConfig Config() => new()
    {
        MaxLadderStage       = 250,
        StageHpBase          = 5000,
        StageHpGrowth        = 1.0493,
        LateRampStartStage   = 200,
        LateRampFinalGrowth  = 2.0,
    };

    [Fact]
    public void Curve_StrictlyRises_AnchorsAt5000_AndNeverOverflows()
    {
        var c = Config();
        long prev = 0;
        for (var n = 1; n <= c.MaxLadderStage; n++)
        {
            long hp = GauntletStageCurve.Hp(n, c);
            hp.Should().BeGreaterThan(prev, $"stage {n} HP must strictly rise (ladder invariant)");
            // The shipped ramped curve tops out at 6.22e16 — 0.67% of long capacity, so the headroom
            // is real, but it does NOT clear the old long.MaxValue/1000 bound (9.22e15) this assertion
            // used to carry. That bound was only ever satisfied by the unramped curve it was written
            // against, which is the point: the one assertion guarding width was guarding the wrong curve.
            hp.Should().BeLessThan(long.MaxValue / 100, $"stage {n} HP must stay far from long overflow");
            prev = hp;
        }
        // The shipped integration test asserts stage-1 baseHp == 5000; the curve must preserve that.
        GauntletStageCurve.Hp(1, c).Should().Be(c.StageHpBase);
    }

    [Fact]
    public void Stage250_BreakEvenPower_MatchesPresumed80MEndgame()
    {
        var c = Config();
        double breakEven = GauntletStageCurve.BreakEvenPower(c.MaxLadderStage, c);

        // ~6.2e15 on the SHIPPED ramped curve. The old assertion here was 60M-100M, which the unramped
        // curve satisfied exactly (8.0e7) — that is where the "stage 250 breaks even at the ~80M endgame
        // power" claim came from. T76's ramp moved the top of the ladder far beyond any reachable
        // battalion power, deliberately: the last fifty stages near-double each. Pinned so a future
        // curve change is loud rather than silent. OWNER: see the frontier note in the test below.
        breakEven.Should().BeInRange(5.0e15, 8.0e15,
            "the shipped ramp puts the top of the ladder far past the endgame power, by design");
    }

    [Fact]
    public void PowerToStage_IsSmooth_NoLowLevelClustering()
    {
        var c = Config();
        // Each ×10 of power should advance ~48 stages (1 / log10(growth)); a smooth, log-uniform mapping
        // means no single band swallows most players at very low levels.
        int s1k   = StageForPower(1_000,     c);
        int s10k  = StageForPower(10_000,    c);
        int s100k = StageForPower(100_000,   c);
        int s1m   = StageForPower(1_000_000, c);
        int s80m  = StageForPower(80_000_000, c);

        (s10k  - s1k ).Should().BeInRange(35, 60);
        (s100k - s10k).Should().BeInRange(35, 60);
        (s1m   - s100k).Should().BeInRange(35, 60);
        // OWNER DECISION POINT. On the unramped curve an 80M-power player reached stage 250 exactly,
        // which is where the "250 is the natural frontier" design note came from. Under the SHIPPED
        // ramp they reach ~213 — the last ~37 stages are gem-pushed territory, not natural progress.
        // That may be exactly what T76 intended; it contradicts the older design note, so it is pinned
        // here rather than left to drift.
        s80m.Should().BeInRange(205, 220,
            "the shipped late ramp moves the natural 80M frontier down from 250 to about 213");
    }

    [Fact]
    public void Print_CurveTable_PowerMap_AndGemOverpush()
    {
        var c = Config();
        _out.WriteLine($"Gauntlet curve  H(n) = {c.StageHpBase} × {c.StageHpGrowth}^(n-1), ramping to ×{c.LateRampFinalGrowth}/stage from {c.LateRampStartStage}   |   StrikesPerDefeat = {c.StrikesPerDefeat}");
        _out.WriteLine("");
        _out.WriteLine("stage |              HP | break-even power |        gold |      xp");
        _out.WriteLine("------+-----------------+------------------+-------------+--------");
        for (var n = 1; n <= c.MaxLadderStage; n += 25)
            PrintRow(c, n);
        PrintRow(c, c.MaxLadderStage);   // always show the ceiling

        _out.WriteLine("");
        _out.WriteLine("Natural Gauntlet level by current battalion power (= highest stage already at break-even):");
        foreach (var p in new double[] { 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 40_000_000, 80_000_000 })
            _out.WriteLine($"   power {p,14:N0}  ->  stage {StageForPower(p, c)}");

        // Gem accelerator: a 40M-power player (mid-game) brute-forcing past their natural wall to 250 by
        // buying strikes (1 gem/strike). Double-power (gem-funded) would HALVE these strike costs.
        _out.WriteLine("");
        double midPower = 40_000_000;
        int wall = StageForPower(midPower, c);
        long gemsToTop = GemsToPush(midPower, wall + 1, c.MaxLadderStage, c);
        long gemsDoubled = GemsToPush(midPower * 2, wall + 1, c.MaxLadderStage, c);
        _out.WriteLine($"A {midPower:N0}-power player's natural wall is stage {wall}.");
        _out.WriteLine($"   Buying strikes to force stages {wall + 1}..{c.MaxLadderStage}: ~{gemsToTop:N0} gems.");
        _out.WriteLine($"   Same push WITH double-power active (effective {midPower * 2:N0}): ~{gemsDoubled:N0} gems.");
    }

    private void PrintRow(GauntletConfig c, int n)
        => _out.WriteLine($"{n,5} | {GauntletStageCurve.Hp(n, c),15:N0} | {GauntletStageCurve.BreakEvenPower(n, c),16:N0} | {GauntletStageCurve.Gold(n, c),11:N0} | {GauntletStageCurve.Xp(n, c),7:N0}");

    // Highest stage already at/under break-even for the given power.
    private static int StageForPower(double power, GauntletConfig c)
    {
        int reached = 0;
        for (var n = 1; n <= c.MaxLadderStage; n++)
        {
            if (GauntletStageCurve.BreakEvenPower(n, c) <= power) reached = n;
            else break;
        }
        return reached;
    }

    // Net strikes (= gems at 1 gem/strike) to clear stages [from..to] at a fixed power: each stage costs
    // ceil(H/power) strikes and refunds StrikesPerDefeat, so only the net positive above break-even is paid.
    private static long GemsToPush(double power, int from, int to, GauntletConfig c)
    {
        long gems = 0;
        for (var n = from; n <= to; n++)
        {
            long cost = (long)System.Math.Ceiling(GauntletStageCurve.Hp(n, c) / power);
            long net = cost - c.StrikesPerDefeat;
            if (net > 0) gems += net;
        }
        return gems;
    }
}
