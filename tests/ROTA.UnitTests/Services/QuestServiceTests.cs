using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class QuestServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private static (QuestService service,
                    Mock<IQuestDefinitionProvider> definitions,
                    Mock<IQuestProgressRepository> questProgress,
                    Mock<IPlayerRepository> players,
                    Mock<IEnergyService> energy,
                    Mock<IGemService> gems,
                    Mock<IAuditLogRepository> auditLog)
        BuildService()
    {
        var definitions  = new Mock<IQuestDefinitionProvider>();
        var questProgress = new Mock<IQuestProgressRepository>();
        var players      = new Mock<IPlayerRepository>();
        var energy       = new Mock<IEnergyService>();
        var gems         = new Mock<IGemService>();
        var auditLog     = new Mock<IAuditLogRepository>();

        var service = new QuestService(
            definitions.Object, questProgress.Object,
            players.Object, energy.Object, gems.Object, auditLog.Object);

        return (service, definitions, questProgress, players, energy, gems, auditLog);
    }

    private static IReadOnlyList<QuestDefinition> TwoQuestChain() => new List<QuestDefinition>
    {
        new() { Id = "q001", Name = "Quest 1", Chapter = 1, EnergyCost = 5,
                GoldReward = 100, ExperienceReward = 50, GemReward = 0, PrerequisiteQuestId = null },
        new() { Id = "q002", Name = "Quest 2", Chapter = 1, EnergyCost = 5,
                GoldReward = 150, ExperienceReward = 75, GemReward = 0, PrerequisiteQuestId = "q001" },
    };

    private static QuestDefinition QuestWithGems(int gemReward = 2) => new()
    {
        Id = "q_gem", Name = "Gem Quest", Chapter = 1, EnergyCost = 5,
        GoldReward = 100, ExperienceReward = 50, GemReward = gemReward, PrerequisiteQuestId = null,
    };

    private static Player MakePlayer(long xp = 0)
    {
        var p = Player.Create("testuser", "test@rota.test", "hash");
        if (xp > 0) p.AddExperience(xp);
        return p;
    }

    private static void SetupSuccessfulEnergySpend(Mock<IEnergyService> energy, Guid playerId)
        => energy.Setup(e => e.SpendEnergyAsync(playerId, ResourceType.Energy, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

    // -----------------------------------------------------------------------
    // GetAvailableQuestsAsync — prerequisite filtering
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAvailableQuests_ReturnsOnlyUnlockedQuests_WhenNoQuestsCompleted()
    {
        var (service, definitions, questProgress, _, _, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        definitions.Setup(d => d.GetAll()).Returns(TwoQuestChain());
        questProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<PlayerQuestProgress>());

        var result = await service.GetAvailableQuestsAsync(playerId);

        result.Should().HaveCount(1, "only q001 has no prerequisite when no quests are completed");
        result[0].Id.Should().Be("q001");
    }

    [Fact]
    public async Task GetAvailableQuests_UnlocksQ002_AfterQ001Completed()
    {
        var (service, definitions, questProgress, _, _, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        var prog = PlayerQuestProgress.Create(playerId, "q001");
        prog.RecordCompletion();

        definitions.Setup(d => d.GetAll()).Returns(TwoQuestChain());
        questProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<PlayerQuestProgress> { prog });

        var result = await service.GetAvailableQuestsAsync(playerId);

        result.Should().HaveCount(2, "q002 is unlocked once q001 is completed");
        result.Should().Contain(r => r.Id == "q002");
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — success path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_Succeeds_SpendingEnergyAndGrantingRewards()
    {
        var (service, definitions, questProgress, players, energy, gems, auditLog) = BuildService();
        var player = MakePlayer();

        var quest = TwoQuestChain()[0]; // q001, no prerequisite
        definitions.Setup(d => d.GetById("q001")).Returns(quest);

        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        SetupSuccessfulEnergySpend(energy, player.Id);

        questProgress.Setup(r => r.GetAsync(player.Id, "q001", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((PlayerQuestProgress?)null);
        questProgress.Setup(r => r.CreateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var result = await service.AttemptQuestAsync(player.Id, "q001");

        result.Success.Should().BeTrue();
        result.GoldGranted.Should().Be(100);
        result.ExperienceGranted.Should().Be(50);
        result.CompletionCount.Should().Be(1);
        energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, 5, It.IsAny<CancellationToken>()), Times.Once);
        questProgress.Verify(r => r.CreateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()), Times.Once);
        auditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — failure: insufficient energy (no side effects)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_ReturnsFailure_WhenEnergyInsufficient_WithNoSideEffects()
    {
        var (service, definitions, questProgress, players, energy, gems, auditLog) = BuildService();
        var player = MakePlayer();

        definitions.Setup(d => d.GetById("q001")).Returns(TwoQuestChain()[0]);
        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false); // insufficient energy

        var result = await service.AttemptQuestAsync(player.Id, "q001");

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.InsufficientEnergy);

        // No rewards granted and no progress written
        players.Verify(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
        questProgress.Verify(r => r.CreateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()), Times.Never);
        questProgress.Verify(r => r.UpdateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()), Times.Never);
        gems.Verify(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — failure: prerequisite not met
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_ReturnsFailure_WhenPrerequisiteNotCompleted()
    {
        var (service, definitions, questProgress, _, energy, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        definitions.Setup(d => d.GetById("q002")).Returns(TwoQuestChain()[1]); // requires q001

        // q001 progress exists but has never been completed
        var prereqProgress = PlayerQuestProgress.Create(playerId, "q001"); // CompletionCount = 0
        questProgress.Setup(r => r.GetAsync(playerId, "q001", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(prereqProgress);

        var result = await service.AttemptQuestAsync(playerId, "q002");

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.PrerequisiteNotMet);
        energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "energy must not be spent before prerequisites are verified");
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — failure: invalid questId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_ReturnsFailure_WhenQuestIdNotFound()
    {
        var (service, definitions, _, _, energy, _, _) = BuildService();

        definitions.Setup(d => d.GetById("unknown")).Returns((QuestDefinition?)null);

        var result = await service.AttemptQuestAsync(Guid.NewGuid(), "unknown");

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.QuestNotFound);
        energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — level up at XP threshold
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_TriggersLevelUp_WhenXpCrossesThreshold()
    {
        var (service, definitions, questProgress, players, energy, _, _) = BuildService();
        // Player at level 1 with 990 XP; quest gives 50 XP → 1040 XP → level 2
        var player = MakePlayer(xp: 990);

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, EnergyCost = 5,
            GoldReward = 0, ExperienceReward = 50, GemReward = 0, PrerequisiteQuestId = null,
        };
        definitions.Setup(d => d.GetById("xp_quest")).Returns(quest);
        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        SetupSuccessfulEnergySpend(energy, player.Id);

        questProgress.Setup(r => r.GetAsync(player.Id, "xp_quest", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((PlayerQuestProgress?)null);
        questProgress.Setup(r => r.CreateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var result = await service.AttemptQuestAsync(player.Id, "xp_quest");

        result.Success.Should().BeTrue();
        result.NewLevel.Should().Be(2, "990 + 50 = 1040 XP crosses the 1000 XP threshold for level 2");
        result.NewExperience.Should().Be(1040);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — gem reward idempotency key format
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_UsesCorrectGemIdempotencyKey_OnFirstCompletion()
    {
        var (service, definitions, questProgress, players, energy, gems, _) = BuildService();
        var player = MakePlayer();
        var quest = QuestWithGems(gemReward: 2);

        definitions.Setup(d => d.GetById("q_gem")).Returns(quest);
        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        SetupSuccessfulEnergySpend(energy, player.Id);

        questProgress.Setup(r => r.GetAsync(player.Id, "q_gem", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((PlayerQuestProgress?)null); // first attempt
        questProgress.Setup(r => r.CreateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await service.AttemptQuestAsync(player.Id, "q_gem");

        var expectedRef = $"quest:q_gem:{player.Id}:1";
        gems.Verify(g => g.GrantGemsAsync(
            player.Id, 2, GemTransactionType.QuestReward, expectedRef,
            It.IsAny<CancellationToken>()), Times.Once,
            $"gem referenceId must be 'quest:{{questId}}:{{playerId}}:{{completionCount}}' — got expected '{expectedRef}'");
    }
}
