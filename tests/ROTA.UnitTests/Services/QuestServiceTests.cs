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

    private record ServiceBundle(
        QuestService Service,
        Mock<IQuestDefinitionProvider> Definitions,
        Mock<IQuestProgressRepository> QuestProgress,
        Mock<IQuestDifficultyProgressRepository> DifficultyProgress,
        Mock<IPlayerRepository> Players,
        Mock<IEnergyService> Energy,
        Mock<IGemService> Gems,
        Mock<IStatService> Stats,
        Mock<ILootTableProvider> LootTables,
        Mock<IItemDefinitionProvider> ItemDefs,
        Mock<IPlayerInventoryRepository> Inventory,
        Mock<IAuditLogRepository> AuditLog,
        Mock<IMagicService> MagicService);

    private static ServiceBundle BuildService(Random? random = null)
    {
        var definitions       = new Mock<IQuestDefinitionProvider>();
        var questProgress     = new Mock<IQuestProgressRepository>();
        var difficultyProgress = new Mock<IQuestDifficultyProgressRepository>();
        var players           = new Mock<IPlayerRepository>();
        var energy            = new Mock<IEnergyService>();
        var gems              = new Mock<IGemService>();
        var stats             = new Mock<IStatService>();
        var lootTables        = new Mock<ILootTableProvider>();
        var itemDefs          = new Mock<IItemDefinitionProvider>();
        var inventory         = new Mock<IPlayerInventoryRepository>();
        var auditLog          = new Mock<IAuditLogRepository>();
        var magicService      = new Mock<IMagicService>();

        // Sane defaults to avoid null-ref in happy-path tests
        difficultyProgress.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<QuestDifficulty>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerQuestDifficultyProgress?)null);
        difficultyProgress.Setup(r => r.CreateAsync(It.IsAny<PlayerQuestDifficultyProgress>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        difficultyProgress.Setup(r => r.UpdateAsync(It.IsAny<PlayerQuestDifficultyProgress>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        stats.Setup(s => s.GrantLevelUpPointsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: 1000 XP per level — keeps existing tests from triggering level-ups
        stats.Setup(s => s.XpToNextLevel(It.IsAny<int>())).Returns(1000);

        inventory.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerInventoryItem?)null);
        inventory.Setup(r => r.CreateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        magicService.Setup(m => m.GrantMagicAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new QuestService(
            definitions.Object, questProgress.Object, difficultyProgress.Object,
            players.Object, energy.Object, gems.Object,
            stats.Object, lootTables.Object, itemDefs.Object, inventory.Object,
            auditLog.Object, magicService.Object, random);

        return new ServiceBundle(service, definitions, questProgress, difficultyProgress,
            players, energy, gems, stats, lootTables, itemDefs, inventory, auditLog, magicService);
    }

    private static IReadOnlyList<QuestDefinition> TwoQuestChain() => new List<QuestDefinition>
    {
        new() { Id = "q001", Name = "Quest 1", Chapter = 1, BaseEnergyCost = 5,
                GoldReward = 100, ExperienceReward = 50, GemReward = 0, PrerequisiteQuestId = null },
        new() { Id = "q002", Name = "Quest 2", Chapter = 1, BaseEnergyCost = 5,
                GoldReward = 150, ExperienceReward = 75, GemReward = 0, PrerequisiteQuestId = "q001" },
    };

    private static QuestDefinition QuestWithGems(int gemReward = 2) => new()
    {
        Id = "q_gem", Name = "Gem Quest", Chapter = 1, BaseEnergyCost = 5,
        GoldReward = 100, ExperienceReward = 50, GemReward = gemReward,
    };

    private static QuestDefinition BossQuest() => new()
    {
        Id = "q_boss", Name = "Boss Quest", Chapter = 1, BaseEnergyCost = 8,
        NodeType = "Boss", GoldReward = 200, ExperienceReward = 100, GemReward = 1,
        SigilDropChance = 0.25f,
        Sigils = new Dictionary<string, string>
        {
            ["Normal"]    = "sigil_ironcolossus_normal",
            ["Hard"]      = "sigil_ironcolossus_hard",
            ["Legendary"] = "sigil_ironcolossus_legendary",
            ["Nightmare"] = "sigil_ironcolossus_nightmare",
        },
    };

    private static Player MakePlayer(long xp = 0)
    {
        var p = Player.Create("testuser", "test@rota.test", "hash");
        if (xp > 0) p.AddExperience(xp, _ => 1000);
        return p;
    }

    private static void SetupPlayerAndEnergy(ServiceBundle b, Player player, bool energySuccess = true)
    {
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);
        b.Players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(energySuccess);
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerQuestProgress?)null);
        b.QuestProgress.Setup(r => r.CreateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // -----------------------------------------------------------------------
    // GetAvailableQuestsAsync — prerequisite filtering
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAvailableQuests_ReturnsOnlyUnlockedQuests_WhenNoQuestsCompleted()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        b.Definitions.Setup(d => d.GetAll()).Returns(TwoQuestChain());
        b.QuestProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestProgress>());

        var result = await b.Service.GetAvailableQuestsAsync(playerId);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("q001");
    }

    [Fact]
    public async Task GetAvailableQuests_UnlocksQ002_AfterQ001Completed()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        var prog = PlayerQuestProgress.Create(playerId, "q001");
        prog.RecordCompletion();

        b.Definitions.Setup(d => d.GetAll()).Returns(TwoQuestChain());
        b.QuestProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestProgress> { prog });

        var result = await b.Service.GetAvailableQuestsAsync(playerId);

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.Id == "q002");
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — Normal difficulty success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_Succeeds_OnNormal_SpendingBaseEnergyCost()
    {
        var b = BuildService();
        var player = MakePlayer();
        var quest = TwoQuestChain()[0];

        b.Definitions.Setup(d => d.GetById("q001")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q001", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.GoldGranted.Should().Be(100);
        result.ExperienceGranted.Should().Be(50);
        result.CompletionCount.Should().Be(1);
        result.Difficulty.Should().Be("Normal");
        result.DifficultyColor.Should().Be("Green");
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, 5, It.IsAny<CancellationToken>()), Times.Once);
        b.AuditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — difficulty energy/reward multipliers
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(QuestDifficulty.Normal,    1.0f, 1.0f,  "Green")]
    [InlineData(QuestDifficulty.Hard,      1.5f, 1.5f,  "Yellow")]
    [InlineData(QuestDifficulty.Legendary, 2.0f, 2.0f,  "Red")]
    [InlineData(QuestDifficulty.Nightmare, 3.0f, 3.5f,  "Purple")]
    public async Task AttemptQuest_AppliesCorrectMultipliers(
        QuestDifficulty difficulty, float energyMult, float rewardMult, string color)
    {
        var b = BuildService();
        var player = MakePlayer();

        // Quest with baseEnergyCost=10, goldReward=100, xpReward=100
        var quest = new QuestDefinition
        {
            Id = "q_multi", Name = "Multi", Chapter = 1, BaseEnergyCost = 10,
            GoldReward = 100, ExperienceReward = 100, GemReward = 0,
        };
        b.Definitions.Setup(d => d.GetById("q_multi")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        // Unlock the difficulty gate by marking prerequisite difficulties complete
        if (difficulty > QuestDifficulty.Normal)
        {
            for (var gd = QuestDifficulty.Normal; gd < difficulty; gd++)
            {
                var gateProg = PlayerQuestDifficultyProgress.Create(player.Id, "q_multi", gd);
                gateProg.RecordCompletion();
                var captured = gd; // avoid closure bug
                b.DifficultyProgress.Setup(r => r.GetAsync(player.Id, "q_multi", captured, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(gateProg);
            }
        }

        int expectedEnergy = (int)Math.Ceiling(10 * energyMult);
        int expectedGold   = (int)(100 * rewardMult);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_multi", difficulty);

        result.Success.Should().BeTrue();
        result.DifficultyColor.Should().Be(color);
        result.GoldGranted.Should().Be(expectedGold);
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, expectedEnergy, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — difficulty gate enforcement
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_RejectsDifficulty_WhenPreviousTierNotCompleted()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        b.Definitions.Setup(d => d.GetById("q001")).Returns(TwoQuestChain()[0]);

        // Normal progress returns null → Hard is locked
        b.DifficultyProgress.Setup(r => r.GetAsync(playerId, "q001", QuestDifficulty.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerQuestDifficultyProgress?)null);

        var result = await b.Service.AttemptQuestAsync(playerId, "q001", QuestDifficulty.Hard);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.DifficultyLocked);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — insufficient energy (no side effects)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_ReturnsFailure_WhenEnergyInsufficient_WithNoSideEffects()
    {
        var b = BuildService();
        var player = MakePlayer();

        b.Definitions.Setup(d => d.GetById("q001")).Returns(TwoQuestChain()[0]);
        SetupPlayerAndEnergy(b, player, energySuccess: false);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q001", QuestDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.InsufficientEnergy);
        b.Players.Verify(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
        b.QuestProgress.Verify(r => r.CreateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Gems.Verify(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — prerequisite not met
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_ReturnsFailure_WhenPrerequisiteNotCompleted()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        b.Definitions.Setup(d => d.GetById("q002")).Returns(TwoQuestChain()[1]);
        var prereq = PlayerQuestProgress.Create(playerId, "q001"); // CompletionCount=0
        b.QuestProgress.Setup(r => r.GetAsync(playerId, "q001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(prereq);

        var result = await b.Service.AttemptQuestAsync(playerId, "q002", QuestDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.PrerequisiteNotMet);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — quest not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_ReturnsFailure_WhenQuestIdNotFound()
    {
        var b = BuildService();

        b.Definitions.Setup(d => d.GetById("unknown")).Returns((QuestDefinition?)null);

        var result = await b.Service.AttemptQuestAsync(Guid.NewGuid(), "unknown", QuestDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.QuestNotFound);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — level-up wiring (AddExperience with milestone formula)
    // XpToNextLevel is mocked to return 1000 (from BuildService default).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_ExactLevelUp_AtThreshold_GrantsLevelUpPoints()
    {
        // Player has 950 XP toward level 2. Quest grants 50 XP. 950+50=1000 = exactly one level.
        var b = BuildService(); // XpToNextLevel → 1000
        var player = MakePlayer(xp: 950); // AddExperience(950, _=>1000): Level=1, Experience=950

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, BaseEnergyCost = 5,
            GoldReward = 0, ExperienceReward = 50, GemReward = 0,
        };
        b.Definitions.Setup(d => d.GetById("xp_quest")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var result = await b.Service.AttemptQuestAsync(player.Id, "xp_quest", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.NewLevel.Should().Be(2);
        // XP carries over: 950+50=1000, level-up consumes 1000 → 0 remaining
        result.CurrentLevelXp.Should().Be(0);
        result.LevelsGained.Should().Be(1);
        b.Stats.Verify(s => s.GrantLevelUpPointsAsync(player.Id, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttemptQuest_XpCarriesOver_AfterLevelUp()
    {
        // Player has 980 XP. Quest grants 50. Total 1030: level up consumes 1000, 30 carries over.
        var b = BuildService(); // XpToNextLevel → 1000
        var player = MakePlayer(xp: 980);

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, BaseEnergyCost = 5,
            GoldReward = 0, ExperienceReward = 50, GemReward = 0,
        };
        b.Definitions.Setup(d => d.GetById("xp_quest")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var result = await b.Service.AttemptQuestAsync(player.Id, "xp_quest", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.NewLevel.Should().Be(2);
        result.CurrentLevelXp.Should().Be(30); // 1030 - 1000 = 30 carry-over
        result.LevelsGained.Should().Be(1);
    }

    [Fact]
    public async Task AttemptQuest_ChainLevelUp_ThreeLevels_FromOneGrant()
    {
        // XpToNextLevel returns 20 → each level costs 20 XP. Quest grants 60 XP → 3 level-ups.
        var b = BuildService();
        b.Stats.Setup(s => s.XpToNextLevel(It.IsAny<int>())).Returns(20); // override default 1000

        var player = MakePlayer(); // Level=1, Experience=0

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, BaseEnergyCost = 5,
            GoldReward = 0, ExperienceReward = 60, GemReward = 0,
        };
        b.Definitions.Setup(d => d.GetById("xp_quest")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var result = await b.Service.AttemptQuestAsync(player.Id, "xp_quest", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.NewLevel.Should().Be(4); // 1→2, 2→3, 3→4
        result.LevelsGained.Should().Be(3);
        result.CurrentLevelXp.Should().Be(0); // 60 = 3×20, nothing left over
        b.Stats.Verify(s => s.GrantLevelUpPointsAsync(player.Id, 2, It.IsAny<CancellationToken>()), Times.Once);
        b.Stats.Verify(s => s.GrantLevelUpPointsAsync(player.Id, 3, It.IsAny<CancellationToken>()), Times.Once);
        b.Stats.Verify(s => s.GrantLevelUpPointsAsync(player.Id, 4, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttemptQuest_GrantLevelUpPointsAsync_CalledOncePerLevelGained()
    {
        // XpToNextLevel → 1000. Quest gives 2500 XP → exactly 2 level-ups with 500 left over.
        var b = BuildService(); // XpToNextLevel → 1000
        var player = MakePlayer(); // Level=1, XP=0

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, BaseEnergyCost = 5,
            GoldReward = 0, ExperienceReward = 2500, GemReward = 0,
        };
        b.Definitions.Setup(d => d.GetById("xp_quest")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var result = await b.Service.AttemptQuestAsync(player.Id, "xp_quest", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.LevelsGained.Should().Be(2); // 2500 / 1000 = 2 full levels, 500 left over
        result.NewLevel.Should().Be(3);
        b.Stats.Verify(s => s.GrantLevelUpPointsAsync(player.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2), "GrantLevelUpPointsAsync called once per level gained, not more");
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — sigil drop (Boss node first completion = guaranteed)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_BossNode_DropsSignature_OnFirstDifficultyCompletion()
    {
        var b = BuildService();
        var player = MakePlayer();
        var boss = BossQuest();

        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(boss);
        SetupPlayerAndEnergy(b, player);

        // No prior difficulty progress → first completion
        b.DifficultyProgress.Setup(r => r.GetAsync(player.Id, "q_boss", QuestDifficulty.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerQuestDifficultyProgress?)null);

        var sigilDef = new ItemDefinition
        {
            Id = "sigil_ironcolossus_normal", Name = "Iron Sigil (Normal)",
            Rarity = ItemRarity.Green, Type = ItemType.Sigil, ArtKey = "sigil_ironcolossus",
        };
        b.ItemDefs.Setup(d => d.GetById("sigil_ironcolossus_normal")).Returns(sigilDef);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_boss", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().ContainSingle(i => i.ItemId == "sigil_ironcolossus_normal",
            "first completion of a Boss node on a new difficulty guarantees the sigil");
        b.Inventory.Verify(r => r.CreateAsync(
            It.Is<PlayerInventoryItem>(i => i.ItemDefinitionId == "sigil_ironcolossus_normal"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — sigil drop (Boss node subsequent — uses RNG)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_BossNode_DropsSignil_OnRepeatCompletion_WhenChanceIs100Pct()
    {
        // SigilDropChance = 1.0 → always drops regardless of RNG seed
        var b = BuildService();
        var player = MakePlayer();
        var boss = new QuestDefinition
        {
            Id = "q_boss", Name = "Boss Quest", Chapter = 1, BaseEnergyCost = 8,
            NodeType = "Boss", GoldReward = 200, ExperienceReward = 100,
            SigilDropChance = 1.0f, // guaranteed drop
            Sigils = new Dictionary<string, string> { ["Normal"] = "sigil_ironcolossus_normal" },
        };

        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(boss);
        SetupPlayerAndEnergy(b, player);

        // First sigil already dropped → tests the repeat-completion chance path
        var priorDiff = PlayerQuestDifficultyProgress.Create(player.Id, "q_boss", QuestDifficulty.Normal);
        priorDiff.RecordCompletion();
        priorDiff.MarkSigilDropped();
        b.DifficultyProgress.Setup(r => r.GetAsync(player.Id, "q_boss", QuestDifficulty.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorDiff);

        var sigilDef = new ItemDefinition
        {
            Id = "sigil_ironcolossus_normal", Name = "Iron Sigil (Normal)",
            Rarity = ItemRarity.Green, Type = ItemType.Sigil, ArtKey = "sigil_ironcolossus",
        };
        b.ItemDefs.Setup(d => d.GetById("sigil_ironcolossus_normal")).Returns(sigilDef);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_boss", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().ContainSingle(i => i.ItemId == "sigil_ironcolossus_normal",
            "SigilDropChance=1.0 means the sigil always drops on repeat completions");
    }

    [Fact]
    public async Task AttemptQuest_BossNode_NeverDropsSigil_OnRepeatCompletion_WhenChanceIsZero()
    {
        var b = BuildService();
        var player = MakePlayer();
        var boss = new QuestDefinition
        {
            Id = "q_boss", Name = "Boss Quest", Chapter = 1, BaseEnergyCost = 8,
            NodeType = "Boss", GoldReward = 200, ExperienceReward = 100,
            SigilDropChance = 0.0f, // never drops after first
            Sigils = new Dictionary<string, string> { ["Normal"] = "sigil_ironcolossus_normal" },
        };

        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(boss);
        SetupPlayerAndEnergy(b, player);

        var priorDiff = PlayerQuestDifficultyProgress.Create(player.Id, "q_boss", QuestDifficulty.Normal);
        priorDiff.RecordCompletion();
        priorDiff.MarkSigilDropped();
        b.DifficultyProgress.Setup(r => r.GetAsync(player.Id, "q_boss", QuestDifficulty.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorDiff);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_boss", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().BeEmpty("SigilDropChance=0.0 means the sigil never drops after the first");
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — loot table items granted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_GrantsGuaranteedLootItems_FromLootTable()
    {
        var b = BuildService();
        var player = MakePlayer();

        var quest = new QuestDefinition
        {
            Id = "q_loot", Name = "Loot Quest", Chapter = 1, BaseEnergyCost = 5,
            GoldReward = 100, ExperienceReward = 50, LootTableId = "lt_q_loot",
        };
        b.Definitions.Setup(d => d.GetById("q_loot")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var lootTable = new LootTableDefinition
        {
            Id = "lt_q_loot", Type = "QuestBoss",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new()
                {
                    GuaranteedDrops = new List<ItemDropChance>
                    {
                        new() { ItemId = "mat_iron_shard", Quantity = 2, Chance = 1.0 },
                    },
                },
            },
        };
        b.LootTables.Setup(l => l.GetById("lt_q_loot")).Returns(lootTable);

        var matDef = new ItemDefinition
        {
            Id = "mat_iron_shard", Name = "Iron Shard", Rarity = ItemRarity.Grey, Type = ItemType.Material, ArtKey = "mat_iron_shard",
        };
        b.ItemDefs.Setup(d => d.GetById("mat_iron_shard")).Returns(matDef);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_loot", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().ContainSingle(i => i.ItemId == "mat_iron_shard" && i.Quantity == 2);
        b.Inventory.Verify(r => r.CreateAsync(
            It.Is<PlayerInventoryItem>(i => i.ItemDefinitionId == "mat_iron_shard"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — gem idempotency key includes difficulty
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_UsesCorrectGemIdempotencyKey_IncludesDifficulty()
    {
        var b = BuildService();
        var player = MakePlayer();
        var quest = QuestWithGems(gemReward: 2);

        b.Definitions.Setup(d => d.GetById("q_gem")).Returns(quest);
        SetupPlayerAndEnergy(b, player);
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.AttemptQuestAsync(player.Id, "q_gem", QuestDifficulty.Normal);

        var expectedRef = $"quest:q_gem:{player.Id}:1:Normal";
        b.Gems.Verify(g => g.GrantGemsAsync(
            player.Id, 2, GemTransactionType.QuestReward, expectedRef,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
