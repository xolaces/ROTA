using FluentAssertions;
using ROTA.Application.Configuration;
using ROTA.Domain.Entities;

namespace ROTA.UnitTests.Balance;

/// <summary>
/// Energy and Stamina must cost the same per unit of XP, whichever way a player buys them.
///
/// WHY THIS NEEDS A TEST. The property holds only because FOUR independent constants agree, and no
/// single file states the relationship:
///
///   PlayerStats.StaminaLsiWeight            2      one Stamina consumes two LSI
///   LevelingConfig.SkillPointCostByStat     2      one Stamina costs two skill points
///   QuestConfig.XpPerEnergyRoll*            1.5    XP per point of Energy spent
///   CombatConfig.XpPerStaminaRoll*          1-5    XP per point of Stamina spent, mean 3.0
///
/// Retuning any ONE of them silently makes a build strictly better, with nothing failing. That
/// already happened: stamina shipped at 1 skill point against an LSI weight of 2, so a stamina build
/// reached the same ceiling for half the points and banked the rest in Attack, Defense and
/// Discernment (fixed in 91c3980). It cost nothing at the time and was invisible until the arithmetic
/// was done by hand.
///
/// These read the CONFIG rather than repeating its numbers, so they fail on the retune rather than on
/// a stale copy of it.
/// </summary>
public class StatParityTests
{
    private static double EnergyXpPerPoint(QuestConfig q)
        => (q.XpPerEnergyRollMin + q.XpPerEnergyRollMax) / 2.0;

    private static double StaminaXpPerPoint(CombatConfig c)
        => (c.XpPerStaminaRollMin + c.XpPerStaminaRollMax) / 2.0;

    [Fact]
    public void XpPerSkillPoint_IsEqualForEnergyAndStamina()
    {
        // The constraint that actually binds. Skill points are earned and spent; the LSI cap is only
        // a ceiling. If a skill point buys more XP through one pool than the other, that pool wins
        // outright and the allocation choice stops being a choice.
        var leveling = new LevelingConfig();
        var quest    = new QuestConfig();
        var combat   = new CombatConfig();

        int energyPrice  = leveling.SkillPointCost("Energy");
        int staminaPrice = leveling.SkillPointCost("Stamina");

        double perSpViaEnergy  = EnergyXpPerPoint(quest)   / energyPrice;
        double perSpViaStamina = StaminaXpPerPoint(combat) / staminaPrice;

        perSpViaStamina.Should().BeApproximately(perSpViaEnergy, 1e-9,
            "a skill point must buy the same XP through either pool — otherwise one build is "
            + "strictly better and the E:S decision is decorative");
    }

    [Fact]
    public void XpPerLsiPoint_IsEqualForEnergyAndStamina()
    {
        // The ceiling-side half of the same property. Equal here but unequal per skill point is
        // exactly the state the game shipped in, so both are asserted rather than one standing in
        // for the other.
        var quest  = new QuestConfig();
        var combat = new CombatConfig();

        double perLsiViaEnergy  = EnergyXpPerPoint(quest);                                   // 1 LSI = 1 energy
        double perLsiViaStamina = StaminaXpPerPoint(combat) / PlayerStats.StaminaLsiWeight;  // 2 LSI = 1 stamina

        perLsiViaStamina.Should().BeApproximately(perLsiViaEnergy, 1e-9,
            "the LSI weighting must cancel the XP rate, or one pool fills the same ceiling with "
            + "more XP behind it");
    }

    [Fact]
    public void StaminaSkillPointPrice_MatchesItsLsiWeight()
    {
        // The root relationship the other two derive from, stated once and directly. If the price
        // ever diverges from the weight again, this is the test that names the cause rather than the
        // symptom.
        var leveling = new LevelingConfig();

        leveling.SkillPointCost("Stamina").Should().Be((int)PlayerStats.StaminaLsiWeight,
            "Stamina's skill-point price is pinned to how much of the LSI budget it consumes — "
            + "they were 1 and 2 respectively, which is what made stamina strictly better");
    }

    [Fact]
    public void EnergyIsTheUnitStat_CostingOneSkillPointPerLsiPoint()
    {
        // Energy is the baseline everything else is expressed against. Owner-locked at 1.
        var leveling = new LevelingConfig();

        leveling.SkillPointCost("Energy").Should().Be(1);
    }

    [Fact]
    public void AFullDrainOfBothPools_YieldsTheSameXpForEveryEnergyStaminaSplit()
    {
        // The player-visible consequence: XP from draining everything is 1.5 x (E + 2S), which
        // depends only on the LSI total and not on how it was split. Checked across the range rather
        // than argued, at a level whose LSI budget divides cleanly.
        const int level = 1000;
        const double lsiCap = 7.45;

        var quest  = new QuestConfig();
        var combat = new CombatConfig();
        double budget = lsiCap * level;

        var yields = new List<double>();
        foreach (double energyShare in new[] { 1.0, 0.75, 0.5, 0.25, 0.0 })
        {
            double energy  = budget * energyShare;
            double stamina = budget * (1 - energyShare) / PlayerStats.StaminaLsiWeight;

            yields.Add(energy * EnergyXpPerPoint(quest) + stamina * StaminaXpPerPoint(combat));
        }

        yields.Should().AllSatisfy(y => y.Should().BeApproximately(yields[0], 1e-6),
            "every split spends the same LSI budget, so every split must return the same XP");
    }
}
