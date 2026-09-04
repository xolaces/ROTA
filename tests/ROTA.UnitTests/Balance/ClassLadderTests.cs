using FluentAssertions;
using ROTA.Application.Configuration;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Balance;

/// <summary>
/// The class ladder's three design rules, which no single file states:
///
///   1. Within a tier every class earns the SAME XP per day — so no branch is simply better.
///   2. Within a tier classes DIFFER in energy/stamina lean — so the choice still means something.
///   3. Every regen interval lands on a five-second boundary — so a countdown reads 1:45, never 1:47.
///
/// Rules 1 and 2 pull against each other, which is why both are asserted. Equal totals alone would
/// make the classes identical; different leans alone is what the ladder shipped with, and it made the
/// Ironguard branch 46% better than the Arcanist branch on the only axis that exists.
/// </summary>
public class ClassLadderTests
{
    private const double XpPerEnergy = 1.5;
    private const double XpPerStamina = 3.0;

    private static ClassConfig Config()
    {
        var cfg = new ClassConfig
        {
            TierSpeedMultiplier = new() { ["Legendary"] = 1.072, ["Ascendant"] = 1.142 },
            RegenMinutesPerPoint = new()
            {
                ["Conscript"]        = new() { ["Energy"] = 2.4167, ["Stamina"] = 4.8333 },
                ["Ironguard"]        = new() { ["Energy"] = 2.5833, ["Stamina"] = 3.1667 },
                ["Sentinel"]         = new() { ["Energy"] = 1.9167, ["Stamina"] = 3.9167 },
                ["Arcanist"]         = new() { ["Energy"] = 1.5833, ["Stamina"] = 5.0833 },
                ["Stormguard"]       = new() { ["Energy"] = 3.0000, ["Stamina"] = 2.3333 },
                ["Bloodguard"]       = new() { ["Energy"] = 2.4167, ["Stamina"] = 2.5833 },
                ["Siegebreaker"]     = new() { ["Energy"] = 2.0000, ["Stamina"] = 2.9167 },
                ["IroncladSentinel"] = new() { ["Energy"] = 1.9167, ["Stamina"] = 3.0833 },
                ["Voidwalker"]       = new() { ["Energy"] = 1.6667, ["Stamina"] = 3.4167 },
                ["Dawnblade"]        = new() { ["Energy"] = 1.5000, ["Stamina"] = 3.7500 },
                ["Runecaller"]       = new() { ["Energy"] = 1.4167, ["Stamina"] = 4.0000 },
                ["ShadowArcanist"]   = new() { ["Energy"] = 1.3333, ["Stamina"] = 4.8333 },
                ["HighArcanist"]     = new() { ["Energy"] = 1.1667, ["Stamina"] = 6.0000 },
            },
        };
        return cfg;
    }

    private static double XpPerDay(ClassConfig c, PlayerClass pc)
    {
        var (e, s) = c.GetRegenRates(pc);
        return (1440 / e) * XpPerEnergy + (1440 / s) * XpPerStamina;
    }

    private static double StaminaShare(ClassConfig c, PlayerClass pc)
    {
        var (e, s) = c.GetRegenRates(pc);
        double fromS = (1440 / s) * XpPerStamina;
        return fromS / ((1440 / e) * XpPerEnergy + fromS);
    }

    private static readonly PlayerClass[] Tier2 =
        { PlayerClass.Ironguard, PlayerClass.Arcanist, PlayerClass.Sentinel };

    private static readonly PlayerClass[] Tier3 =
    {
        PlayerClass.Stormguard, PlayerClass.Bloodguard, PlayerClass.Siegebreaker,
        PlayerClass.HighArcanist, PlayerClass.ShadowArcanist, PlayerClass.Runecaller,
        PlayerClass.IroncladSentinel, PlayerClass.Voidwalker, PlayerClass.Dawnblade,
    };

    [Fact]
    public void WithinTier2_EveryClassEarnsTheSameXpPerDay()
    {
        var c = Config();
        var totals = Tier2.Select(pc => XpPerDay(c, pc)).ToList();

        (totals.Max() / totals.Min() - 1).Should().BeLessThan(0.03,
            "a level-5 choice that changes throughput is not a choice — it is a correct answer");
    }

    [Fact]
    public void WithinTier3_EveryClassEarnsTheSameXpPerDay()
    {
        // This is the one that shipped broken: Bloodguard beat HighArcanist by 46%.
        var c = Config();
        var totals = Tier3.Select(pc => XpPerDay(c, pc)).ToList();

        (totals.Max() / totals.Min() - 1).Should().BeLessThan(0.05,
            "the level-100 specialisation must not decide how fast the player levels");
    }

    [Fact]
    public void WithinEachTier_ClassesStillDifferInTheirLean()
    {
        // The counterweight to the two tests above. Equalising totals must not collapse the classes
        // into copies of each other — the difference moves to WHAT they regenerate.
        var c = Config();

        var t2 = Tier2.Select(pc => StaminaShare(c, pc)).ToList();
        (t2.Max() - t2.Min()).Should().BeGreaterThan(0.15,
            "Ironguard leans stamina, Arcanist leans energy, Sentinel sits between");

        var t3 = Tier3.Select(pc => StaminaShare(c, pc)).ToList();
        (t3.Max() - t3.Min()).Should().BeGreaterThan(0.35,
            "nine specialisations should span the lean axis, not cluster on it");
    }

    [Fact]
    public void EveryRegenInterval_LandsOnAFiveSecondBoundary()
    {
        // So a countdown reads 1:45 or 2:10, never 1:47. Asserted through GetRegenRates rather than
        // against the config, because the tier multiplier below would otherwise reintroduce fractions.
        var c = Config();

        foreach (var pc in Enum.GetValues<PlayerClass>())
        {
            var (e, s) = c.GetRegenRates(pc);
            foreach (var minutes in new[] { e, s })
            {
                int seconds = (int)Math.Round(minutes * 60);
                (seconds % 5).Should().Be(0,
                    $"{pc} regen of {minutes} min = {seconds}s must sit on a five-second boundary");
            }
        }
    }

    [Fact]
    public void TheAutoTiers_ActuallyGrantSomething()
    {
        // Legendary (L500) and Ascendant (L1000) used to be name prefixes only — GetRegenRates
        // stripped them and returned the tier-3 rates, so regen did not move between level 100 and
        // level 2,000. Nineteen hundred levels across two promotions.
        var c = Config();

        double t3   = XpPerDay(c, PlayerClass.Stormguard);
        double leg  = XpPerDay(c, PlayerClass.LegendaryStormguard);
        double asc  = XpPerDay(c, PlayerClass.AscendantStormguard);

        leg.Should().BeGreaterThan(t3 * 1.03, "Legendary must be a real step, not a title");
        asc.Should().BeGreaterThan(leg * 1.03, "Ascendant must be a real step over Legendary");
    }

    [Fact]
    public void TheAutoTiers_DoNotDisturbTheClassLean()
    {
        // A tier speeds a class up; it must not turn a stamina class into an energy one.
        var c = Config();

        foreach (var (bas, leg) in new[]
                 {
                     (PlayerClass.Stormguard,   PlayerClass.LegendaryStormguard),
                     (PlayerClass.HighArcanist, PlayerClass.LegendaryHighArcanist),
                     (PlayerClass.Voidwalker,   PlayerClass.AscendantVoidwalker),
                 })
        {
            StaminaShare(c, leg).Should().BeApproximately(StaminaShare(c, bas), 0.04,
                $"{leg} is {bas} moving faster, not a different class");
        }
    }

    [Fact]
    public void TheLadderNeverGoesBackwards()
    {
        // Every promotion must be an upgrade. A 37.5% speed-up applied only below Luminary once made
        // Bloodguard faster than Luminary, so converging at level 2,000 was a downgrade.
        var c = Config();

        double bestTier3 = Tier3.Max(pc => XpPerDay(c, pc));
        double bestAuto  = XpPerDay(c, PlayerClass.AscendantBloodguard);

        bestAuto.Should().BeGreaterThan(bestTier3,
            "Ascendant must beat every tier-3 class it can be built from");
    }
}
