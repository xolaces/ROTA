using FluentAssertions;
using Moq;
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
        Mock<IAuditLogRepository> AuditLog);

    private static ServiceBundle BuildService()
    {
        var players  = new Mock<IPlayerRepository>();
        var energy   = new Mock<IEnergyService>();
        var gems     = new Mock<IGemService>();
        var auditLog = new Mock<IAuditLogRepository>();

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        energy.Setup(e => e.UpdateMaxAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        players.Setup(p => p.UpdateStatsAsync(It.IsAny<PlayerStats>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new ServiceBundle(new StatService(players.Object, energy.Object, gems.Object, auditLog.Object),
            players, energy, gems, auditLog);
    }

    // Creates a player that has FindByIdWithStatsAsync returning it with fully initialised stats
    private static Player MakePlayerWithStats(int level = 1, int skillPoints = 10)
    {
        var player = Player.Create("testuser", "test@rota.test", "hash");
        // Advance to desired level
        for (int i = 1; i < level; i++)
            player.AddExperience(1000);

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
}
