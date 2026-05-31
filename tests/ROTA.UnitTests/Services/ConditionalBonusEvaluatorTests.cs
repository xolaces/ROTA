using FluentAssertions;
using ROTA.Application.Models;
using ROTA.Application.Services;

namespace ROTA.UnitTests.Services;

public class ConditionalBonusEvaluatorTests
{
    // -----------------------------------------------------------------------
    // OwnedUnitCount — floor division
    // -----------------------------------------------------------------------

    [Fact]
    public void Evaluate_OwnedUnitCount_FloorDivision_StacksCorrectly()
    {
        // owns 25, perCount=10 → floor(25/10) = 2 stacks; FlatAttack=5 → +10
        var bonuses = new[]
        {
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedUnitCount,
                ConditionTarget = "troop_farmhand",
                PerCount        = 10,
                BonusType       = BonusType.FlatAttack,
                BonusAmount     = 5,
            },
        };
        var ownedById  = new Dictionary<string, int> { ["troop_farmhand"] = 25 };
        var ownedByTag = new Dictionary<string, int>();
        var slots      = new HashSet<string>();

        var result = ConditionalBonusEvaluator.Evaluate(bonuses, ownedById, ownedByTag, slots);

        result.FlatAttack.Should().Be(10, "floor(25/10) = 2 stacks × 5 = 10");
    }

    [Fact]
    public void Evaluate_OwnedUnitCount_ZeroOwned_NoBonus()
    {
        var bonuses = new[]
        {
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedUnitCount,
                ConditionTarget = "troop_farmhand",
                PerCount        = 10,
                BonusType       = BonusType.FlatAttack,
                BonusAmount     = 5,
            },
        };
        var ownedById  = new Dictionary<string, int>();
        var ownedByTag = new Dictionary<string, int>();
        var slots      = new HashSet<string>();

        var result = ConditionalBonusEvaluator.Evaluate(bonuses, ownedById, ownedByTag, slots);

        result.FlatAttack.Should().Be(0, "no units owned → 0 stacks");
    }

    [Fact]
    public void Evaluate_OwnedUnitCount_BelowThreshold_ZeroStacks()
    {
        // owns 5, perCount=10 → floor(5/10) = 0 stacks
        var bonuses = new[]
        {
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedUnitCount,
                ConditionTarget = "troop_farmhand",
                PerCount        = 10,
                BonusType       = BonusType.FlatAttack,
                BonusAmount     = 5,
            },
        };
        var ownedById  = new Dictionary<string, int> { ["troop_farmhand"] = 5 };
        var ownedByTag = new Dictionary<string, int>();
        var slots      = new HashSet<string>();

        var result = ConditionalBonusEvaluator.Evaluate(bonuses, ownedById, ownedByTag, slots);

        result.FlatAttack.Should().Be(0, "5 < perCount(10) → 0 stacks");
    }

    // -----------------------------------------------------------------------
    // OwnedTypeCount — tag accumulation
    // -----------------------------------------------------------------------

    [Fact]
    public void Evaluate_OwnedTypeCount_AccumulatesAcrossItems()
    {
        // 3 of item A + 2 of item B, both tagged "material" → total 5, perCount=5 → 1 stack
        // FlatDefense=3/stack → +3
        var bonuses = new[]
        {
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedTypeCount,
                ConditionTarget = "material",
                PerCount        = 5,
                BonusType       = BonusType.FlatDefense,
                BonusAmount     = 3,
            },
        };
        var ownedById  = new Dictionary<string, int> { ["mat_a"] = 3, ["mat_b"] = 2 };
        var ownedByTag = new Dictionary<string, int> { ["material"] = 5 }; // pre-aggregated by EquipmentService
        var slots      = new HashSet<string>();

        var result = ConditionalBonusEvaluator.Evaluate(bonuses, ownedById, ownedByTag, slots);

        result.FlatDefense.Should().Be(3, "floor(5/5)=1 stack × 3 = 3");
    }

    // -----------------------------------------------------------------------
    // EquippedSlot — binary check
    // -----------------------------------------------------------------------

    [Fact]
    public void Evaluate_EquippedSlot_Occupied_GivesBonus()
    {
        var bonuses = new[]
        {
            new ConditionalBonus
            {
                ConditionType   = ConditionType.EquippedSlot,
                ConditionTarget = "Mount",
                PerCount        = 1,
                BonusType       = BonusType.FlatDamagePercent,
                BonusAmount     = 0.1,
            },
        };
        var ownedById  = new Dictionary<string, int>();
        var ownedByTag = new Dictionary<string, int>();
        var slots      = new HashSet<string> { "mount" }; // lower-invariant

        var result = ConditionalBonusEvaluator.Evaluate(bonuses, ownedById, ownedByTag, slots);

        result.FlatDamagePercent.Should().BeApproximately(0.1, 1e-9, "Mount slot is occupied → 1 stack");
    }

    [Fact]
    public void Evaluate_EquippedSlot_Empty_NoBonus()
    {
        var bonuses = new[]
        {
            new ConditionalBonus
            {
                ConditionType   = ConditionType.EquippedSlot,
                ConditionTarget = "Mount",
                PerCount        = 1,
                BonusType       = BonusType.FlatDamagePercent,
                BonusAmount     = 0.1,
            },
        };
        var ownedById  = new Dictionary<string, int>();
        var ownedByTag = new Dictionary<string, int>();
        var slots      = new HashSet<string>(); // no slots equipped

        var result = ConditionalBonusEvaluator.Evaluate(bonuses, ownedById, ownedByTag, slots);

        result.FlatDamagePercent.Should().Be(0, "Mount slot is empty → 0 stacks");
    }

    // -----------------------------------------------------------------------
    // Multiple bonus types accumulated in one call
    // -----------------------------------------------------------------------

    [Fact]
    public void Evaluate_MultipleBonusTypes_AllAccumulated()
    {
        var bonuses = new[]
        {
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedUnitCount,
                ConditionTarget = "unit_a",
                PerCount        = 1,
                BonusType       = BonusType.FlatAttack,
                BonusAmount     = 2,
            },
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedUnitCount,
                ConditionTarget = "unit_a",
                PerCount        = 1,
                BonusType       = BonusType.FlatDefense,
                BonusAmount     = 1,
            },
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedUnitCount,
                ConditionTarget = "unit_a",
                PerCount        = 1,
                BonusType       = BonusType.ProcChanceFlat,
                BonusAmount     = 0.1,
            },
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedUnitCount,
                ConditionTarget = "unit_a",
                PerCount        = 1,
                BonusType       = BonusType.ProcAmountFlat,
                BonusAmount     = 0.5,
            },
            new ConditionalBonus
            {
                ConditionType   = ConditionType.OwnedUnitCount,
                ConditionTarget = "unit_a",
                PerCount        = 1,
                BonusType       = BonusType.FlatDamagePercent,
                BonusAmount     = 0.2,
            },
        };
        var ownedById  = new Dictionary<string, int> { ["unit_a"] = 3 }; // 3 stacks each
        var ownedByTag = new Dictionary<string, int>();
        var slots      = new HashSet<string>();

        var result = ConditionalBonusEvaluator.Evaluate(bonuses, ownedById, ownedByTag, slots);

        result.FlatAttack.Should().Be(6,       "3 stacks × 2 = 6 ATK");
        result.FlatDefense.Should().Be(3,      "3 stacks × 1 = 3 DEF");
        result.ProcChanceFlat.Should().BeApproximately(0.3, 1e-9, "3 stacks × 0.1 = 0.3 proc chance");
        result.ProcAmountFlat.Should().BeApproximately(1.5, 1e-9, "3 stacks × 0.5 = 1.5 proc amount");
        result.FlatDamagePercent.Should().BeApproximately(0.6, 1e-9, "3 stacks × 0.2 = 0.6 dmg%");
    }

    [Fact]
    public void Evaluate_EmptyBonusList_ReturnsAllZeros()
    {
        var result = ConditionalBonusEvaluator.Evaluate(
            Array.Empty<ConditionalBonus>(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            new HashSet<string>());

        result.FlatAttack.Should().Be(0);
        result.FlatDefense.Should().Be(0);
        result.ProcChanceFlat.Should().Be(0);
        result.ProcAmountFlat.Should().Be(0);
        result.FlatDamagePercent.Should().Be(0);
    }
}
