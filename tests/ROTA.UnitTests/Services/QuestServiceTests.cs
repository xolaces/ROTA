using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
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
        Mock<IMagicService> MagicService,
        Mock<ILegionService> LegionService,
        Mock<IEquipmentService> Equipment,
        Mock<IMasteryService> Mastery,
        Mock<IAchievementService> Achievements);

    private static ServiceBundle BuildService(Random? random = null)
    {
        var definitions       = new Mock<IQuestDefinitionProvider>();
        // Default GetAll → empty list so the T45 zone-boss gate (which scans for in-zone siblings)
        // never NPEs in tests that don't set up the full definition list. Tests that exercise the
        // gate / zone-reset override this with an explicit list.
        definitions.Setup(d => d.GetAll()).Returns(new List<QuestDefinition>());
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
        var legionService     = new Mock<ILegionService>();
        var equipment         = new Mock<IEquipmentService>();

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
        // Default: no-op for unit/legion grant drops
        legionService.Setup(l => l.GrantUnitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        legionService.Setup(l => l.GrantLegionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: no-op for gear drops
        equipment.Setup(e => e.GrantGearAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var questConfig = Options.Create(new QuestConfig());
        var mastery = new Mock<IMasteryService>();
        // Neutral loot modifiers by default → quest rewards/drops unchanged unless a test overrides.
        mastery.Setup(m => m.GetLootModifiersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryLootModifiers(1.0, 1.0, 1.0, 0.0));
        var achievements = new Mock<IAchievementService>();

        var service = new QuestService(
            definitions.Object, questProgress.Object, difficultyProgress.Object,
            players.Object, energy.Object, gems.Object,
            stats.Object, lootTables.Object, itemDefs.Object, inventory.Object,
            auditLog.Object, magicService.Object, legionService.Object, equipment.Object,
            mastery.Object, achievements.Object, questConfig, random);

        return new ServiceBundle(service, definitions, questProgress, difficultyProgress,
            players, energy, gems, stats, lootTables, itemDefs, inventory, auditLog, magicService,
            legionService, equipment, mastery, achievements);
    }

    // Ch1 Z0 chain. ZoneIndex 0 → battle zoneRatio = XpZoneRatioBase (1.2); chapter-1 scalar = 1.0.
    private static IReadOnlyList<QuestDefinition> TwoQuestChain() => new List<QuestDefinition>
    {
        new() { Id = "q001", Name = "Quest 1", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 0,
                BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50, GemReward = 0, PrerequisiteQuestId = null },
        new() { Id = "q002", Name = "Quest 2", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 1,
                BaseEnergyCost = 5, GoldReward = 150, ExperienceReward = 75, GemReward = 0, PrerequisiteQuestId = "q001" },
    };

    private static QuestDefinition QuestWithGems(int gemReward = 2) => new()
    {
        Id = "q_gem", Name = "Gem Quest", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 0,
        BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50, GemReward = gemReward,
    };

    private static QuestDefinition BossQuest() => new()
    {
        Id = "q_boss", Name = "Boss Quest", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 1,
        BaseEnergyCost = 8, NodeType = "Boss", GoldReward = 200, ExperienceReward = 100, GemReward = 1,
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
        // Regression: every returned node is past the prerequisite filter, so it must be flagged
        // unlocked. A missing/false IsUnlocked deserializes to false on the client and disables
        // every Attempt button — the original play-blocking bug.
        result[0].IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task GetAvailableQuests_UnlocksQ002_OnlyAfterQ001Cleared()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        // Completing q001 once is NOT enough now — the node must be fully depleted (Cleared).
        var partial = PlayerQuestProgress.Create(playerId, "q001");
        partial.RecordCompletion();
        partial.Deplete(5);   // 95 remaining → not cleared

        b.Definitions.Setup(d => d.GetAll()).Returns(TwoQuestChain());
        b.QuestProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestProgress> { partial });

        var stillLocked = await b.Service.GetAvailableQuestsAsync(playerId);
        stillLocked.Should().ContainSingle().Which.Id.Should().Be("q001");

        // Deplete q001 to 0 → Cleared → q002 unlocks.
        partial.Deplete(100);
        partial.IsCleared.Should().BeTrue();

        var unlocked = await b.Service.GetAvailableQuestsAsync(playerId);
        unlocked.Should().HaveCount(2);
        unlocked.Should().Contain(r => r.Id == "q002");
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
        // T44 — XP = base(50) × zoneRatio(1.2 @ ZoneIndex 0) × chapterScalar(1.0 @ Ch1) × rewardMult(1.0) = 60.
        result.ExperienceGranted.Should().Be(60);
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
    // AttemptQuestAsync — node depletion (System 20)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_DepletesBattleNode_By5_OnFreshAttempt()
    {
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q001")).Returns(TwoQuestChain()[0]);
        SetupPlayerAndEnergy(b, player); // QuestProgress.GetAsync → null (fresh node, starts at 100)

        var result = await b.Service.AttemptQuestAsync(player.Id, "q001", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.NodeProgress.Should().Be(95.0);   // 100 − 5
        result.NodeCleared.Should().BeFalse();
        result.NodeJustCleared.Should().BeFalse();
    }

    [Fact]
    public async Task AttemptQuest_DepletesBossNode_By2Point5()
    {
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(BossQuest());
        SetupPlayerAndEnergy(b, player);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_boss", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.NodeProgress.Should().Be(97.5);   // 100 − 2.5 (boss depletes slower)
        result.NodeCleared.Should().BeFalse();
    }

    [Fact]
    public async Task AttemptQuest_ClearsNode_AndFlagsJustCleared_OnFinalDepletion()
    {
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q001")).Returns(TwoQuestChain()[0]);
        SetupPlayerAndEnergy(b, player);

        // Existing node with only 5 progress left — one more battle attempt clears it.
        var nearlyDone = PlayerQuestProgress.Create(player.Id, "q001");
        nearlyDone.Deplete(95); // 5 remaining, not yet cleared
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "q001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(nearlyDone);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q001", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.NodeProgress.Should().Be(0.0);
        result.NodeCleared.Should().BeTrue();
        result.NodeJustCleared.Should().BeTrue();
        b.QuestProgress.Verify(r => r.UpdateAsync(It.IsAny<PlayerQuestProgress>(), It.IsAny<CancellationToken>()), Times.Once);

        // TICKET 46 — a non-boss node clear records QuestNodesCleared (not QuestBossesCleared) and evaluates.
        b.Achievements.Verify(a => a.RecordProgressAsync(
            player.Id, AchievementMetric.QuestNodesCleared, 1, null, It.IsAny<CancellationToken>()), Times.Once);
        b.Achievements.Verify(a => a.RecordProgressAsync(
            It.IsAny<Guid>(), AchievementMetric.QuestBossesCleared, It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Achievements.Verify(a => a.EvaluateCompletionsAsync(player.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttemptQuest_AlreadyClearedNode_IsRejected_NoSideEffects()
    {
        // T26 — a cleared (locked) node can't be attempted until the chapter boss resets it.
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q001")).Returns(TwoQuestChain()[0]);
        SetupPlayerAndEnergy(b, player);

        var cleared = PlayerQuestProgress.Create(player.Id, "q001");
        cleared.Deplete(100); // already at 0 / cleared
        cleared.IsCleared.Should().BeTrue();
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "q001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cleared);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q001", QuestDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.NodeCleared);
        // Guard runs before any side effects — no energy spent.
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttemptQuest_BossCompletion_ResetsOnlyItsZone_PreservingUnlocks_LeavingOtherZoneUntouched()
    {
        // T44/45 (revises T26) — clearing a ZONE boss resets only that zone's nodes back to fresh
        // (Progress→start, IsCleared→false) while keeping HasEverCleared (forward unlocks survive),
        // and leaves a DIFFERENT zone in the same chapter completely untouched.
        var b = BuildService();
        var player = MakePlayer();

        // Ch1: Z0 = { q001 battle, q_boss boss }, Z1 = { z1_node battle, z1_boss boss }.
        var z0Battle = new QuestDefinition { Id = "q001", Name = "Z0 Battle", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 0, BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50 };
        var z0Boss   = BossQuest(); // Ch1 Z0 NodeIndex 1
        var z1Battle = new QuestDefinition { Id = "z1_node", Name = "Z1 Battle", Chapter = 1, ZoneIndex = 1, ZoneName = "Z1", NodeIndex = 0, BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50, PrerequisiteQuestId = "q_boss" };
        var z1Boss   = new QuestDefinition { Id = "z1_boss", Name = "Z1 Boss", Chapter = 1, ZoneIndex = 1, ZoneName = "Z1", NodeIndex = 1, NodeType = "Boss", BaseEnergyCost = 8, GoldReward = 200, ExperienceReward = 100, PrerequisiteQuestId = "z1_node" };

        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(z0Boss);
        b.Definitions.Setup(d => d.GetAll())
            .Returns(new List<QuestDefinition> { z0Battle, z0Boss, z1Battle, z1Boss });
        SetupPlayerAndEnergy(b, player);

        // Z0 sibling battle — previously cleared; should be RESTORED by the zone reset.
        var battle = PlayerQuestProgress.Create(player.Id, "q001");
        battle.Deplete(100); // cleared (HasEverCleared latched)
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "q001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(battle);

        // The Z0 boss with 2.5 progress left — one boss attempt (depletes 2.5) clears it.
        var boss = PlayerQuestProgress.Create(player.Id, "q_boss");
        boss.Deplete(97.5);
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "q_boss", It.IsAny<CancellationToken>()))
            .ReturnsAsync(boss);

        // A node in a DIFFERENT zone (Z1) — previously cleared; must be LEFT UNTOUCHED by the reset.
        var otherZoneNode = PlayerQuestProgress.Create(player.Id, "z1_node");
        otherZoneNode.Deplete(100); // cleared
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "z1_node", It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherZoneNode);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_boss", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.NodeJustCleared.Should().BeTrue();
        result.ZoneReset.Should().BeTrue();

        // Z0 nodes fresh again, but the permanent unlock latch is preserved.
        battle.IsCleared.Should().BeFalse();
        battle.Progress.Should().Be(100.0);
        battle.HasEverCleared.Should().BeTrue("a zone reset must never re-lock earned progression");
        boss.IsCleared.Should().BeFalse();
        boss.HasEverCleared.Should().BeTrue();

        // The other zone's node is NOT reset — zone-scoped reset only.
        otherZoneNode.IsCleared.Should().BeTrue("a different zone in the chapter is left untouched");
        otherZoneNode.Progress.Should().Be(0.0);
    }

    [Fact]
    public async Task AttemptQuest_BlockedByPrerequisite_UntilPrereqCleared()
    {
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q002")).Returns(TwoQuestChain()[1]);
        SetupPlayerAndEnergy(b, player);

        // Prereq q001 exists but only partially depleted → q002 still blocked.
        var prereq = PlayerQuestProgress.Create(player.Id, "q001");
        prereq.Deplete(50); // 50 remaining, not cleared
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "q001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(prereq);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q002", QuestDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.PrerequisiteNotMet);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
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
        // Boss in Ch1 Z0 → XP multiplier = XpBossRatio(2.0) × chapterScalar(1.0) = 2.0, so base 25 → 50.
        var b = BuildService(); // XpToNextLevel → 1000
        var player = MakePlayer(xp: 950); // AddExperience(950, _=>1000): Level=1, Experience=950

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, ZoneIndex = 0, NodeType = "Boss",
            BaseEnergyCost = 5, GoldReward = 0, ExperienceReward = 25, GemReward = 0,
        };
        b.Definitions.Setup(d => d.GetById("xp_quest")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var result = await b.Service.AttemptQuestAsync(player.Id, "xp_quest", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ExperienceGranted.Should().Be(50);
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
        // Boss in Ch1 Z0 → ×2.0 multiplier, so base 25 → 50 XP.
        var b = BuildService(); // XpToNextLevel → 1000
        var player = MakePlayer(xp: 980);

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, ZoneIndex = 0, NodeType = "Boss",
            BaseEnergyCost = 5, GoldReward = 0, ExperienceReward = 25, GemReward = 0,
        };
        b.Definitions.Setup(d => d.GetById("xp_quest")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var result = await b.Service.AttemptQuestAsync(player.Id, "xp_quest", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ExperienceGranted.Should().Be(50);
        result.NewLevel.Should().Be(2);
        result.CurrentLevelXp.Should().Be(30); // 1030 - 1000 = 30 carry-over
        result.LevelsGained.Should().Be(1);
    }

    [Fact]
    public async Task AttemptQuest_ChainLevelUp_ThreeLevels_FromOneGrant()
    {
        // XpToNextLevel returns 20 → each level costs 20 XP. Quest grants 60 XP → 3 level-ups.
        // Boss in Ch1 Z0 → ×2.0 multiplier, so base 30 → 60 XP.
        var b = BuildService();
        b.Stats.Setup(s => s.XpToNextLevel(It.IsAny<int>())).Returns(20); // override default 1000

        var player = MakePlayer(); // Level=1, Experience=0

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, ZoneIndex = 0, NodeType = "Boss",
            BaseEnergyCost = 5, GoldReward = 0, ExperienceReward = 30, GemReward = 0,
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
        // Boss in Ch1 Z0 → ×2.0 multiplier, so base 1250 → 2500 XP.
        var b = BuildService(); // XpToNextLevel → 1000
        var player = MakePlayer(); // Level=1, XP=0

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, ZoneIndex = 0, NodeType = "Boss",
            BaseEnergyCost = 5, GoldReward = 0, ExperienceReward = 1250, GemReward = 0,
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

    // -----------------------------------------------------------------------
    // System 22 Phase A Slice 7 — Discernment drop-quality (rarity-upgrade)
    // -----------------------------------------------------------------------

    private QuestDefinition ChanceDropQuest(string lootTableId = "lt_qual") => new()
    {
        Id = "q_qual", Name = "Quality Quest", Chapter = 1, BaseEnergyCost = 5,
        GoldReward = 100, ExperienceReward = 50, LootTableId = lootTableId,
    };

    private void SetupChanceDropLoot(ServiceBundle b, string itemId, string? upgradesTo, string lootTableId = "lt_qual")
    {
        var lootTable = new LootTableDefinition
        {
            Id = lootTableId, Type = "Quest",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new()
                {
                    ChanceDrops = new List<ItemDropChance> { new() { ItemId = itemId, Quantity = 1, Chance = 1.0 } },
                },
            },
        };
        b.LootTables.Setup(l => l.GetById(lootTableId)).Returns(lootTable);
        b.ItemDefs.Setup(d => d.GetById(itemId)).Returns(new ItemDefinition
        {
            Id = itemId, Name = itemId, Rarity = ItemRarity.Grey, Type = ItemType.Material, UpgradesTo = upgradesTo,
        });
        if (upgradesTo is not null)
            b.ItemDefs.Setup(d => d.GetById(upgradesTo)).Returns(new ItemDefinition
            {
                Id = upgradesTo, Name = upgradesTo, Rarity = ItemRarity.White, Type = ItemType.Material,
            });
    }

    [Fact]
    public async Task AttemptQuest_DiscernmentQuality_UpgradesFiredChanceDrop()
    {
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q_qual")).Returns(ChanceDropQuest());
        SetupPlayerAndEnergy(b, player);
        SetupChanceDropLoot(b, "mat_base", upgradesTo: "mat_upgraded");
        // Quality ×1.0 → a fired drop always upgrades.
        b.Mastery.Setup(m => m.GetLootModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryLootModifiers(1.0, 1.0, 1.0, 1.0));

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_qual", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().ContainSingle(i => i.ItemId == "mat_upgraded", "quality roll upgrades the drop");
        result.ItemsGranted.Should().NotContain(i => i.ItemId == "mat_base");
    }

    [Fact]
    public async Task AttemptQuest_DiscernmentQuality_NoUpgradesTo_GrantsBase()
    {
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q_qual")).Returns(ChanceDropQuest());
        SetupPlayerAndEnergy(b, player);
        SetupChanceDropLoot(b, "mat_base", upgradesTo: null); // no upgrade path
        b.Mastery.Setup(m => m.GetLootModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryLootModifiers(1.0, 1.0, 1.0, 1.0));

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_qual", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().ContainSingle(i => i.ItemId == "mat_base", "no upgradesTo → never upgrades");
    }

    [Fact]
    public async Task AttemptQuest_DiscernmentQuality_ZeroChance_GrantsBase()
    {
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q_qual")).Returns(ChanceDropQuest());
        SetupPlayerAndEnergy(b, player);
        SetupChanceDropLoot(b, "mat_base", upgradesTo: "mat_upgraded");
        // Default neutral loot mods (DiscernmentQualityChance = 0) → no upgrade.

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_qual", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().ContainSingle(i => i.ItemId == "mat_base", "zero quality chance → base item");
    }

    // -----------------------------------------------------------------------
    // System 22 Phase A Slice 6 — Hoard (gold) + Discernment (sigil-find)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_HoardGoldMultiplier_BoostsGoldReward()
    {
        var b = BuildService();
        var player = MakePlayer();
        var quest = TwoQuestChain()[0]; // GoldReward = 100
        b.Definitions.Setup(d => d.GetById("q001")).Returns(quest);
        SetupPlayerAndEnergy(b, player);
        // Hoard gold ×2 (the other lanes neutral).
        b.Mastery.Setup(m => m.GetLootModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryLootModifiers(1.0, 2.0, 1.0, 0.0));

        var result = await b.Service.AttemptQuestAsync(player.Id, "q001", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.GoldGranted.Should().Be(200, "Hoard ×2 on a 100-gold quest");
    }

    [Fact]
    public async Task AttemptQuest_DiscernmentSigilFind_RaisesPostFirstClearChanceToGuaranteed()
    {
        var b = BuildService();
        var player = MakePlayer();
        // Base 0.5 (RNG-dependent on its own) × 2.0 sigil-find → effective min(1.0, 1.0) = guaranteed drop.
        var boss = new QuestDefinition
        {
            Id = "q_boss", Name = "Boss Quest", Chapter = 1, BaseEnergyCost = 8,
            NodeType = "Boss", GoldReward = 200, ExperienceReward = 100,
            SigilDropChance = 0.5f,
            Sigils = new Dictionary<string, string> { ["Normal"] = "sigil_ironcolossus_normal" },
        };
        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(boss);
        SetupPlayerAndEnergy(b, player);

        var priorDiff = PlayerQuestDifficultyProgress.Create(player.Id, "q_boss", QuestDifficulty.Normal);
        priorDiff.RecordCompletion();
        priorDiff.MarkSigilDropped(); // first-clear sigil already taken → exercises the chance path
        b.DifficultyProgress.Setup(r => r.GetAsync(player.Id, "q_boss", QuestDifficulty.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorDiff);
        b.ItemDefs.Setup(d => d.GetById("sigil_ironcolossus_normal")).Returns(new ItemDefinition
        {
            Id = "sigil_ironcolossus_normal", Name = "Iron Sigil", Rarity = ItemRarity.Green, Type = ItemType.Sigil,
        });
        // Discernment sigil-find ×2 → 0.5 base becomes a guaranteed (clamped 1.0) drop.
        b.Mastery.Setup(m => m.GetLootModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryLootModifiers(1.0, 1.0, 2.0, 0.0));

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_boss", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().ContainSingle(i => i.ItemId == "sigil_ironcolossus_normal",
            "sigil-find ×2 raises the 0.5 base chance to a guaranteed drop");
    }

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

    // -----------------------------------------------------------------------
    // G2: QuestService gear drop wiring
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_GrantsGearDrop_WhenLootTableHasGearDropWithChanceOne()
    {
        var b = BuildService();
        var player = MakePlayer();

        var quest = new QuestDefinition
        {
            Id = "q_gear_loot", Name = "Gear Loot Quest", Chapter = 1, BaseEnergyCost = 5,
            GoldReward = 100, ExperienceReward = 50, LootTableId = "lt_q_gear",
        };
        b.Definitions.Setup(d => d.GetById("q_gear_loot")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        var lootTable = new LootTableDefinition
        {
            Id = "lt_q_gear", Type = "QuestBoss",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new()
                {
                    GearDrops = new List<GearDropChance>
                    {
                        new() { GearDefinitionId = "gear_conscript_helm", Quantity = 1, Chance = 1.0 },
                    },
                },
            },
        };
        b.LootTables.Setup(l => l.GetById("lt_q_gear")).Returns(lootTable);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_gear_loot", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        b.Equipment.Verify(e => e.GrantGearAsync(
            player.Id, "gear_conscript_helm", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // AttemptQuestAsync — Discernment-scaled chance drops (System 20 Slice 2)
    // -----------------------------------------------------------------------

    // Returns a fixed roll from NextDouble() so chance thresholds are deterministic.
    private sealed class FixedRandom : Random
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public override double NextDouble() => _value;
    }

    private static (ServiceBundle b, Player player) GearDropFixture(double roll, int discernment)
    {
        var b = BuildService(new FixedRandom(roll));
        var player = MakePlayer();

        var quest = new QuestDefinition
        {
            Id = "q_disc", Name = "Discernment Quest", Chapter = 1, BaseEnergyCost = 5,
            GoldReward = 0, ExperienceReward = 0, LootTableId = "lt_disc",
        };
        b.Definitions.Setup(d => d.GetById("q_disc")).Returns(quest);
        SetupPlayerAndEnergy(b, player);

        // One gear chance-drop at a 10% base rate.
        var lootTable = new LootTableDefinition
        {
            Id = "lt_disc", Type = "Quest",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new()
                {
                    GearDrops = new List<GearDropChance>
                    {
                        new() { GearDefinitionId = "gear_pano_helm", Quantity = 1, Chance = 0.10 },
                    },
                },
            },
        };
        b.LootTables.Setup(l => l.GetById("lt_disc")).Returns(lootTable);
        b.Stats.Setup(s => s.GetStatsAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerStatsResponse { DiscernmentInvestment = discernment });

        return (b, player);
    }

    [Fact]
    public async Task AttemptQuest_GearDrop_DoesNotDrop_AtBaseChance_WhenRollExceedsIt()
    {
        // base 0.10, Discernment 0 → effective 0.10; roll 0.15 ≥ 0.10 → no drop.
        var (b, player) = GearDropFixture(roll: 0.15, discernment: 0);

        await b.Service.AttemptQuestAsync(player.Id, "q_disc", QuestDifficulty.Normal);

        b.Equipment.Verify(e => e.GrantGearAsync(
            It.IsAny<Guid>(), "gear_pano_helm", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttemptQuest_GearDrop_Drops_WhenDiscernmentRaisesChanceAboveTheSameRoll()
    {
        // base 0.10, Discernment 30 → 0.10 × (1 + 30×0.03) = 0.19; same roll 0.15 < 0.19 → drops.
        var (b, player) = GearDropFixture(roll: 0.15, discernment: 30);

        await b.Service.AttemptQuestAsync(player.Id, "q_disc", QuestDifficulty.Normal);

        b.Equipment.Verify(e => e.GrantGearAsync(
            player.Id, "gear_pano_helm", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // T44/T55 — zone-indexed XP formula
    // XP = base × zoneRatio × chapterXpMultiplier × rewardMult.
    //   battle zoneRatio = XpZoneRatioBase(1.2) + ZoneIndex × XpZoneRatioPerZone(0.05)
    //   boss   zoneRatio = XpBossRatio(2.0) regardless of zone
    // T55: the per-chapter scalar is now ChapterScaling[ch].XpMultiplier (gentle — the base XP already
    // carries chapter progression, and energy now co-scales). Defaults: Ch1=1.00, Ch2=1.03.
    // rewardMult Normal=1.0, Nightmare=3.5.
    // -----------------------------------------------------------------------

    private static QuestDefinition XpNode(
        int chapter, int zoneIndex, string nodeType = "Battle", int experienceReward = 100)
        => new()
        {
            Id = "q_xp", Name = "XP Node", Chapter = chapter, ZoneIndex = zoneIndex, ZoneName = "Z",
            NodeIndex = 0, NodeType = nodeType, BaseEnergyCost = 5,
            GoldReward = 0, ExperienceReward = experienceReward, GemReward = 0,
        };

    [Theory]
    // base 100, Ch1 (scalar 1.0):
    [InlineData(1, 0, "Battle", QuestDifficulty.Normal,    120)]  // 100 × 1.2  × 1.0 × 1.0
    [InlineData(1, 2, "Battle", QuestDifficulty.Normal,    130)]  // 100 × 1.30 × 1.0 × 1.0  (1.2 + 2×0.05)
    [InlineData(1, 0, "Boss",   QuestDifficulty.Normal,    200)]  // 100 × 2.0  × 1.0 × 1.0
    [InlineData(1, 2, "Boss",   QuestDifficulty.Normal,    200)]  // boss ratio ignores zone
    // Ch2 (T55 XpMultiplier 1.03):
    [InlineData(2, 0, "Battle", QuestDifficulty.Normal,    123)]  // (int)(100 × 1.2  × 1.03 × 1.0) = 123
    [InlineData(2, 0, "Boss",   QuestDifficulty.Normal,    206)]  // (int)(100 × 2.0  × 1.03 × 1.0) = 206
    // Nightmare rewardMult 3.5, Ch1:
    [InlineData(1, 0, "Battle", QuestDifficulty.Nightmare, 420)]  // 100 × 1.2  × 1.0 × 3.5
    [InlineData(1, 0, "Boss",   QuestDifficulty.Nightmare, 700)]  // 100 × 2.0  × 1.0 × 3.5
    public async Task AttemptQuest_XpFormula_ScalesByZoneChapterAndDifficulty(
        int chapter, int zoneIndex, string nodeType, QuestDifficulty difficulty, int expectedXp)
    {
        var b = BuildService();
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q_xp")).Returns(XpNode(chapter, zoneIndex, nodeType));
        SetupPlayerAndEnergy(b, player);

        // Unlock the difficulty gate for non-Normal difficulties.
        if (difficulty > QuestDifficulty.Normal)
        {
            for (var gd = QuestDifficulty.Normal; gd < difficulty; gd++)
            {
                var gateProg = PlayerQuestDifficultyProgress.Create(player.Id, "q_xp", gd);
                gateProg.RecordCompletion();
                var captured = gd;
                b.DifficultyProgress.Setup(r => r.GetAsync(player.Id, "q_xp", captured, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(gateProg);
            }
        }

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_xp", difficulty);

        result.Success.Should().BeTrue();
        result.ExperienceGranted.Should().Be(expectedXp);
        result.XpGained.Should().Be(expectedXp);
    }

    // -----------------------------------------------------------------------
    // T45 — zone-boss gate (a per-zone boss requires all preceding zone nodes cleared)
    // -----------------------------------------------------------------------

    // Ch1 Z0 = { zn0 battle, zn1 battle, zb boss }. The boss prereq is the last battle.
    private static IReadOnlyList<QuestDefinition> ZoneWithBoss() => new List<QuestDefinition>
    {
        new() { Id = "zn0", Name = "Node 0", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 0,
                BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50, PrerequisiteQuestId = null },
        new() { Id = "zn1", Name = "Node 1", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 1,
                BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50, PrerequisiteQuestId = "zn0" },
        new() { Id = "zb", Name = "Zone Boss", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 2,
                NodeType = "Boss", BaseEnergyCost = 8, GoldReward = 200, ExperienceReward = 100, GemReward = 1,
                PrerequisiteQuestId = "zn1" },
    };

    [Fact]
    public async Task AttemptQuest_ZoneBoss_Rejected_WhenASiblingNodeNotEverCleared_NoEnergySpent()
    {
        var b = BuildService();
        var player = MakePlayer();
        var defs = ZoneWithBoss();
        b.Definitions.Setup(d => d.GetById("zb")).Returns(defs[2]);
        b.Definitions.Setup(d => d.GetAll()).Returns(defs);
        SetupPlayerAndEnergy(b, player);

        // The boss's own prereq (zn1) is cleared so the prerequisite check passes — but zn0 was never
        // cleared, so the ZONE gate must still reject (proves the gate is a distinct, stronger check).
        var zn1 = PlayerQuestProgress.Create(player.Id, "zn1");
        zn1.Deplete(100); // cleared → HasEverCleared
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "zn1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(zn1);
        // zn0 never attempted → null → not cleared.
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "zn0", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerQuestProgress?)null);

        var result = await b.Service.AttemptQuestAsync(player.Id, "zb", QuestDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.ZoneBossLocked);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttemptQuest_ZoneBoss_Succeeds_WhenAllSiblingNodesEverCleared()
    {
        var b = BuildService();
        var player = MakePlayer();
        var defs = ZoneWithBoss();
        b.Definitions.Setup(d => d.GetById("zb")).Returns(defs[2]);
        b.Definitions.Setup(d => d.GetAll()).Returns(defs);
        SetupPlayerAndEnergy(b, player);

        foreach (var id in new[] { "zn0", "zn1" })
        {
            var p = PlayerQuestProgress.Create(player.Id, id);
            p.Deplete(100); // cleared → HasEverCleared
            b.QuestProgress.Setup(r => r.GetAsync(player.Id, id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(p);
        }

        var result = await b.Service.AttemptQuestAsync(player.Id, "zb", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // T45 — cross-zone ordering (a zone's first node is prereq-locked on the previous zone's boss)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AttemptQuest_NextZoneFirstNode_LockedUntilPreviousZoneBossEverCleared()
    {
        var b = BuildService();
        var player = MakePlayer();

        // Z1 first node requires the Z0 boss "zb".
        var z1First = new QuestDefinition
        {
            Id = "z1n0", Name = "Zone 1 Node 0", Chapter = 1, ZoneIndex = 1, ZoneName = "Z1", NodeIndex = 0,
            BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50, PrerequisiteQuestId = "zb",
        };
        b.Definitions.Setup(d => d.GetById("z1n0")).Returns(z1First);
        SetupPlayerAndEnergy(b, player);

        // Z0 boss exists but was never cleared → Z1 first node stays locked.
        var zbProgress = PlayerQuestProgress.Create(player.Id, "zb");
        zbProgress.Deplete(2.5); // attempted once, far from cleared
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "zb", It.IsAny<CancellationToken>()))
            .ReturnsAsync(zbProgress);

        var locked = await b.Service.AttemptQuestAsync(player.Id, "z1n0", QuestDifficulty.Normal);
        locked.Success.Should().BeFalse();
        locked.FailureCode.Should().Be(QuestFailureCode.PrerequisiteNotMet);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        // Clear the Z0 boss → HasEverCleared latches → Z1 first node unlocks.
        zbProgress.Deplete(100);
        zbProgress.HasEverCleared.Should().BeTrue();

        var unlocked = await b.Service.AttemptQuestAsync(player.Id, "z1n0", QuestDifficulty.Normal);
        unlocked.Success.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // T45 — availability DTO carries zone fields + greys a boss whose zone isn't depleted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAvailableQuests_PopulatesZoneFields_AndGreysBoss_UntilZoneDepleted()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var defs = ZoneWithBoss(); // zn0, zn1 battles + zb boss, all Ch1 Z0
        b.Definitions.Setup(d => d.GetAll()).Returns(defs);

        // zn0 cleared (unlocks zn1 + the boss is visible), zn1 NOT yet cleared → boss stays greyed.
        var zn0 = PlayerQuestProgress.Create(playerId, "zn0");
        zn0.Deplete(100); // cleared
        b.QuestProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestProgress> { zn0 });

        var result = await b.Service.GetAvailableQuestsAsync(playerId);

        var node0 = result.Single(q => q.Id == "zn0");
        node0.ZoneName.Should().Be("Z0");
        node0.ZoneIndex.Should().Be(0);
        node0.NodeIndex.Should().Be(0);
        node0.IsUnlocked.Should().BeTrue();

        // The boss is returned (its prereq zn1 isn't cleared, so it should NOT be returned at all).
        // zn1 itself is returned and unlocked; the boss requires zn1 cleared → not in the list yet.
        result.Should().Contain(q => q.Id == "zn1");
        result.Single(q => q.Id == "zn1").IsUnlocked.Should().BeTrue();
        result.Should().NotContain(q => q.Id == "zb",
            "the boss's prerequisite (zn1) is not yet cleared, so it is not yet surfaced");
    }

    [Fact]
    public async Task GetAvailableQuests_BossSurfacedButGreyed_WhenPrereqClearedButZoneSiblingPending()
    {
        // Construct a zone where the boss's prereq is cleared but an EARLIER sibling is not, so the
        // boss is surfaced (prereq satisfied) yet greyed (IsUnlocked=false) by the zone gate.
        var b = BuildService();
        var playerId = Guid.NewGuid();

        // Boss prereq is zn0 (not zn1) so we can clear the prereq while leaving zn1 pending.
        var zn0 = new QuestDefinition { Id = "zn0", Name = "N0", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 0, BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50 };
        var zn1 = new QuestDefinition { Id = "zn1", Name = "N1", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 1, BaseEnergyCost = 5, GoldReward = 100, ExperienceReward = 50, PrerequisiteQuestId = "zn0" };
        var zb  = new QuestDefinition { Id = "zb", Name = "Boss", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0", NodeIndex = 2, NodeType = "Boss", BaseEnergyCost = 8, GoldReward = 200, ExperienceReward = 100, PrerequisiteQuestId = "zn0" };
        b.Definitions.Setup(d => d.GetAll()).Returns(new List<QuestDefinition> { zn0, zn1, zb });

        // Only zn0 cleared → boss prereq satisfied (surfaced) but zn1 pending → boss greyed.
        var zn0Prog = PlayerQuestProgress.Create(playerId, "zn0");
        zn0Prog.Deplete(100);
        b.QuestProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestProgress> { zn0Prog });

        var result = await b.Service.GetAvailableQuestsAsync(playerId);

        var boss = result.Single(q => q.Id == "zb");
        boss.IsBossNode.Should().BeTrue();
        boss.IsUnlocked.Should().BeFalse("the zone still has an uncleared node (zn1), so the boss is greyed");
    }
}
