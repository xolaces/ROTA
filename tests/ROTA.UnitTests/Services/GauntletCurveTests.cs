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

    // Defaults already match appsettings (base 5000, growth 1.0493, StrikesPerDefeat 10); only the ladder
    // length is opt-in, so set it here to exercise the full 250-stage curve.
    private static GauntletConfig Config() => new() { MaxLadderStage = 250 };

    [Fact]
    public void Curve_StrictlyRises_AnchorsAt5000_AndNeverOverflows()
    {
        var c = Config();
        long prev = 0;
        for (var n = 1; n <= c.MaxLadderStage; n++)
        {
            long hp = GauntletStageCurve.Hp(n, c);
            hp.Should().BeGreaterThan(prev, $"stage {n} HP must strictly rise (ladder invariant)");
            hp.Should().BeLessThan(long.MaxValue / 1000, $"stage {n} HP must stay far from long overflow");
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
        breakEven.Should().BeInRange(60_000_000, 100_000_000,
            "stage 250 should break even near the ~80M endgame battalion power so 250 is the natural frontier");
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
        s80m.Should().BeInRange(240, 250, "the ~80M endgame power should reach (about) the top of the ladder");
    }

    [Fact]
    public void Print_CurveTable_PowerMap_AndGemOverpush()
    {
        var c = Config();
        _out.WriteLine($"Gauntlet curve  H(n) = {c.StageHpBase} × {c.StageHpGrowth}^(n-1)   |   StrikesPerDefeat = {c.StrikesPerDefeat}   |   ~{(int)(1.0 / System.Math.Log10(c.StageHpGrowth))} stages per ×10 power");
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

    // ── helpers ──────────────────────────────────────────────────────────────

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
