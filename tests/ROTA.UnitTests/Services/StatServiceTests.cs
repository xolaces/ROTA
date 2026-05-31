using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class StatServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private record ServiceBundle(
        StatService Service,
        Mock<IPlayerRepository> Players,
        Mock<IEnergyService> Energy,
        Mock<IGemService> Gems,
        Mock<IAuditLogRepository> AuditLog,
        Mock<IClassService> Classes,
        Mock<IEquipmentService> Equipment);

    private static IOptions<LevelingConfig> DefaultLevelingConfig() =>
        Options.Create(new LevelingConfig
        {
            XpBaseMultiplier = 30.0,
            XpExponent = 0.7,
            MilestoneFloors = new Dictionary<int, int>
            {
                [100]   = 500,
                [500]   = 3000,
                [1000]  = 15000,
                [2500]  = 35000,
                [5000]  = 75000,
                [10000] = 200000,
            }
        });

    private static IOptions<CombatConfig> DefaultCombatConfig() =>
        Options.Create(new CombatConfig
        {
            BaseCritChance            = 0.05,
            MaxCritChanceBonus        = 0.10,
            CritChancePerDiscernment  = 0.0001,
            BaseCritMultiplier        = 1.5,
            MaxCritDamageBonus        = 1.0,
            CritDamagePerDiscernment  = 0.0002,
        });

    private static ServiceBundle BuildService()
    {
        var players   = new Mock<IPlayerRepository>();
        var energy    = new Mock<IEnergyService>();
        var gems      = new Mock<IGemService>();
        var auditLog  = new Mock<IAuditLogRepository>();
        var classes   = new Mock<IClassService>();
        var equipment = new Mock<IEquipmentService>();

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        energy.Setup(e => e.UpdateMaxAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        players.Setup(p => p.UpdateStatsAsync(It.IsAny<PlayerStats>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: no auto-advance (returns current class unchanged)
        classes.Setup(c => c.ComputeAutoAdvance(It.IsAny<int>(), It.IsAny<PlayerClass>()))
            .Returns((int _, PlayerClass current) => current);
        // Default: pass-through — no gear bonus
        equipment.Setup(e => e.GetEffectiveCombatDataAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int atk, int def, CancellationToken _) =>
                new EffectiveCombatData(atk, def, null, 0.0));

        return new ServiceBundle(
            new StatService(players.Object, energy.Object, gems.Object, auditLog.Object,
                DefaultLevelingConfig(), DefaultCombatConfig(), classes.Object, equipment.Object),
            players, energy, gems, auditLog, classes, equipment);
    }

    // Creates a player that has FindByIdWithStatsAsync returning it with fully initialised stats
    private static Player MakePlayerWithStats(int level = 1, int skillPoints = 10)
    {
        var player = Player.Create("testuser", "test@rota.test", "hash");
        // Advance to desired level using a flat 1000 XP/level function (test-only formula)
        for (int i = 1; i < level; i++)
            player.AddExperience(1000, _ => 1000);

        var stats = PlayerStats.Create(player.Id);
        stats.AddSkillPoints(skillPoints);
        // Attach stats via the navigation property (reflection, since private setter)
        typeof(Player).GetProperty("Stats")!
            .SetValue(player, stats);
        return player;
    }

    private static void SetupPlayer(ServiceBundle b, Player player)
    {
        b.Players.Setup(p => p.FindByIdWithStatsAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);
    }

    // -----------------------------------------------------------------------
    // AllocateStatPointAsync — happy path
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(StatType.Attack)]
    [InlineData(StatType.Defense)]
    [InlineData(StatType.Health)]
    [InlineData(StatType.Discernment)]
    public async Task AllocateStat_Succeeds_DeductingSkillPoints(StatType statType)
    {
        var b = BuildService();
        var player = MakePlayerWithStats(level: 1, skillPoints: 5);
        SetupPlayer(b, player);

        var result = await b.Service.AllocateStatPointAsync(player.Id, statType, 3);

        result.Success.Should().BeTrue();
        result.NewSkillPointsRemaining.Should().Be(2);
        b.Players.Verify(p => p.UpdateStatsAsync(player.Stats!, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // AllocateStatPointAsync — LSI cap enforced
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AllocateEnergy_RejectsAllocation_WhenLsiCapWouldBeExceeded()
    {
        // Level 1, LSI cap = 9.0
        // LSI = (energyInvestment + stamina*2) / level
        // Allocating 10 to Energy at level 1 → LSI = 10/1 = 10.0 > 9.0 → reject
        var b = BuildService();
        var player = MakePlayerWithStats(level: 1, skillPoints: 20);
        SetupPlayer(b, player);

        var result = await b.Service.AllocateStatPointAsync(player.Id, StatType.Energy, 10);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("LSI cap");
        b.Players.Verify(p => p.UpdateStatsAsync(It.IsAny<PlayerStats>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllocateEnergy_Succeeds_WhenAllocationStaysUnderLsiCap()
    {
        // Level 5, LSI cap = 9.0
        // Allocating 9 to Energy → LSI = 9/5 = 1.8 < 9.0 → allowed
        var b = BuildService();
        var player = MakePlayerWithStats(level: 5, skillPoints: 20);
        SetupPlayer(b, player);

        var result = await b.Service.AllocateStatPointAsync(player.Id, StatType.Energy, 9);

        result.Success.Should().BeTrue();
        result.NewEnergyInvestment.Should().Be(9);
        b.Energy.Verify(e => e.UpdateMaxAsync(player.Id, ResourceType.Energy, 19, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AllocateStamina_RejectsAllocation_WhenLsiCapWouldBeExceeded()
    {
        // Stamina counts double: LSI = (0 + stamina*2) / 1
        // Allocating 5 to Stamina at level 1 → LSI = 10/1 = 10.0 > 9.0 → reject
        var b = BuildService();
        var player = MakePlayerWithStats(level: 1, skillPoints: 20);
        SetupPlayer(b, player);

        var result = await b.Service.AllocateStatPointAsync(player.Id, StatType.Stamina, 5);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("LSI cap");
    }

    // -----------------------------------------------------------------------
    // AllocateStatPointAsync — insufficient skill points
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AllocateStat_Fails_WhenSkillPointsInsufficient()
    {
        var b = BuildService();
        var player = MakePlayerWithStats(level: 5, skillPoints: 2);
        SetupPlayer(b, player);

        var result = await b.Service.AllocateStatPointAsync(player.Id, StatType.Attack, 5);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Insufficient");
        b.Players.Verify(p => p.UpdateStatsAsync(It.IsAny<PlayerStats>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // GrantLevelUpPointsAsync — +10 SP per level
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(15, true)]
    public async Task GrantLevelUpPoints_Grants10SP_AndGemsAtMultiplesOf5(int level, bool expectGems)
    {
        var b = BuildService();
        var player = MakePlayerWithStats(level: level, skillPoints: 0);
        SetupPlayer(b, player);
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.GrantLevelUpPointsAsync(player.Id, level);

        player.Stats!.SkillPoints.Should().Be(10);

        if (expectGems)
        {
            var expectedRef = $"levelup:gems:{player.Id}:{level}";
            b.Gems.Verify(g => g.GrantGemsAsync(
                player.Id, 5, GemTransactionType.LevelUpReward, expectedRef,
                It.IsAny<CancellationToken>()), Times.Once,
                $"level {level} is divisible by 5 so 5 gems should be granted");
        }
        else
        {
            b.Gems.Verify(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
                $"level {level} is not divisible by 5 so no gems should be granted");
        }
    }

    // -----------------------------------------------------------------------
    // AddUnassignedPointsAsync — raid/item stat point rewards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddUnassignedPoints_AddsToSkillPoints_WithoutLsiCheck()
    {
        var b = BuildService();
        var player = MakePlayerWithStats(level: 1, skillPoints: 0);
        SetupPlayer(b, player);

        await b.Service.AddUnassignedPointsAsync(player.Id, 7);

        player.Stats!.SkillPoints.Should().Be(7);
        b.Players.Verify(p => p.UpdateStatsAsync(player.Stats, It.IsAny<CancellationToken>()), Times.Once);
        // No LSI check — these can exceed cap via items/raids (only manual allocation is capped)
        b.Energy.Verify(e => e.UpdateMaxAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // XpToNextLevel — formula and milestone floors
    // Formula: Math.Max(floor, (int)Math.Round(30.0 × level^0.7))
    // -----------------------------------------------------------------------

    [Fact]
    public void XpToNextLevel_Level1_ReturnsFormulaValue_NoFloor()
    {
        // 30.0 × 1^0.7 = 30.0 → round = 30; no milestone floor applies at level 1 → result = 30
        var b = BuildService();
        b.Service.XpToNextLevel(1).Should().Be(30);
    }

    [Fact]
    public void XpToNextLevel_Level99_ReturnsFormulaValue_BelowMilestoneKey100()
    {
        // Level 99 < milestone key 100, so floor = 0. Formula: 30 × 99^0.7 ≈ 748.
        // Verify: result is the formula value, no floor constrains it.
        var b = BuildService();
        var result = b.Service.XpToNextLevel(99);
        result.Should().BeGreaterThan(0);
        result.Should().BeLessThan(3000); // well below the milestone-500 floor
        // Exact formula value (within 1 of expected due to double precision)
        result.Should().BeInRange(745, 755);
    }

    [Fact]
    public void XpToNextLevel_Level100_ReturnsFormulaValue_FloorDoesNotConstrain()
    {
        // Milestone floor at key 100 is 500. Formula at 100: 30 × 100^0.7 ≈ 754 > 500.
        // Formula value wins — floor is set but does not constrain at this level.
        var b = BuildService();
        var result = b.Service.XpToNextLevel(100);
        result.Should().BeGreaterThanOrEqualTo(500); // floor is the minimum guarantee
        result.Should().BeInRange(750, 760);          // formula value (~754) wins
    }

    [Fact]
    public void XpToNextLevel_Level500_ReturnsMilestoneFloor_FormulaIsBelow()
    {
        // Formula at 500: 30 × 500^0.7 ≈ 2326. Floor at milestone 500 = 3000. Floor kicks in.
        var b = BuildService();
        b.Service.XpToNextLevel(500).Should().Be(3000);
    }

    [Fact]
    public void XpToNextLevel_Level1000_ReturnsMilestoneFloor_FormulaIsBelow()
    {
        // Formula at 1000: 30 × 1000^0.7 ≈ 3777. Floor at milestone 1000 = 15000. Floor kicks in.
        var b = BuildService();
        b.Service.XpToNextLevel(1000).Should().Be(15000);
    }

    [Fact]
    public void XpToNextLevel_Level999_ReturnsFormulaValue_NotLevel1000Floor()
    {
        // Level 999: highest matching milestone is key 500 (floor 3000). Formula ≈ 3774 > 3000.
        // Formula wins. Must NOT return 15000 (which is the level-1000 floor).
        var b = BuildService();
        var result = b.Service.XpToNextLevel(999);
        result.Should().NotBe(15000);
        result.Should().BeGreaterThan(3000); // formula exceeds the milestone-500 floor
        result.Should().BeInRange(3770, 3780);
    }

    // -----------------------------------------------------------------------
    // GetCritProfile — discernment crit chance and multiplier
    // Formulas:
    //   Chance     = 0.05 + min(0.10, discernment × 0.0001)   cap at 0.15
    //   Multiplier = 1.50 + min(1.00, discernment × 0.0002)   cap at 2.50
    // -----------------------------------------------------------------------

    [Fact]
    public void GetCritProfile_ZeroDiscernment_ReturnsBaseValues()
    {
        // Chance = 0.05 + min(0.10, 0) = 0.05; Multiplier = 1.5 + min(1.0, 0) = 1.5
        var b = BuildService();
        var profile = b.Service.GetCritProfile(0);
        profile.Chance.Should().BeApproximately(0.05, 1e-9);
        profile.Multiplier.Should().BeApproximately(1.5, 1e-9);
    }

    [Fact]
    public void GetCritProfile_1000Discernment_HitsCritChanceCap()
    {
        // At 1000 discernment: chance bonus = 1000 × 0.0001 = 0.10 = MaxCritChanceBonus → cap reached
        // Chance = 0.05 + 0.10 = 0.15 (max); Multiplier = 1.5 + min(1.0, 0.2) = 1.7
        var b = BuildService();
        var profile = b.Service.GetCritProfile(1000);
        profile.Chance.Should().BeApproximately(0.15, 1e-9, "1000 discernment hits the 0.10 crit chance bonus cap");
        profile.Multiplier.Should().BeApproximately(1.7, 1e-9);
    }

    [Fact]
    public void GetCritProfile_5000Discernment_HitsCritDamageCap()
    {
        // At 5000 discernment: damage bonus = 5000 × 0.0002 = 1.0 = MaxCritDamageBonus → cap reached
        // Chance = 0.15 (capped); Multiplier = 1.5 + 1.0 = 2.5 (max)
        var b = BuildService();
        var profile = b.Service.GetCritProfile(5000);
        profile.Chance.Should().BeApproximately(0.15, 1e-9);
        profile.Multiplier.Should().BeApproximately(2.5, 1e-9, "5000 discernment hits the 1.0 crit damage bonus cap");
    }

    [Fact]
    public void GetCritProfile_100000Discernment_StillCappedAtBothMaxima()
    {
        // Far beyond any cap — both should remain at their hard ceilings
        var b = BuildService();
        var profile = b.Service.GetCritProfile(100000);
        profile.Chance.Should().BeApproximately(0.15, 1e-9, "chance must never exceed BaseCritChance + MaxCritChanceBonus");
        profile.Multiplier.Should().BeApproximately(2.5, 1e-9, "multiplier must never exceed BaseCritMultiplier + MaxCritDamageBonus");
    }

    [Fact]
    public void GetCritProfile_MidDiscernment_InterpolatesCorrectly()
    {
        // At 500 discernment: chance bonus = 500 × 0.0001 = 0.05
        // Chance = 0.05 + 0.05 = 0.10; Multiplier = 1.5 + min(1.0, 0.10) = 1.60
        var b = BuildService();
        var profile = b.Service.GetCritProfile(500);
        profile.Chance.Should().BeApproximately(0.10, 1e-9);
        profile.Multiplier.Should().BeApproximately(1.60, 1e-9);
    }
}
