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
using ROTA.UnitTests.TestSupport;

namespace ROTA.UnitTests.Services;

public class QuestServiceTests
{
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

    private static ServiceBundle BuildService(Random? random = null, QuestConfig? questConfig = null)
    {
        var definitions       = new Mock<IQuestDefinitionProvider>();
        // Default GetAll → empty list so the T45 zone-boss gate (which scans for in-zone siblings)
        // never NPEs in tests that don't set up the full definition list. Tests that exercise the
        // gate / zone-reset override this with an explicit list.
        definitions.Setup(d => d.GetAll()).Returns(new List<QuestDefinition>());
        var questProgress     = new Mock<IQuestProgressRepository>();
        var difficultyProgress = new Mock<IQuestDifficultyProgressRepository>();
        var players           = new Mock<IPlayerRepository>();
        players.SetupMutatePassThrough();   // T59 — reward writes route through MutateWithRetryAsync
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
        difficultyProgress.Setup(r => r.GetAllForPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestDifficultyProgress>());
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

        var questConfigOptions = Options.Create(questConfig ?? new QuestConfig());
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
            mastery.Object, achievements.Object,
            new ROTA.UnitTests.TestSupport.PassThroughPlayerMutationLock(), questConfigOptions, random);

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

    // Owner 2026-06-14 — XP = summed roll(min..max) per energy spent. Pinning min==max collapses the roll
    // to a constant, so XP = energyCost × perEnergy exactly (deterministic, no Random dependence).
    private static QuestConfig PinnedXp(double perEnergy)
        => new() { XpPerEnergyRollMin = perEnergy, XpPerEnergyRollMax = perEnergy };

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

    // GetAvailableQuestsAsync — prerequisite filtering

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

    // GetAvailableQuestsAsync — T74 difficulty unlock hint

    [Theory]
    [InlineData(new string[0], "Normal")]
    [InlineData(new[] { "Normal" }, "Hard")]
    [InlineData(new[] { "Normal", "Hard" }, "Legendary")]
    [InlineData(new[] { "Normal", "Hard", "Legendary" }, "Nightmare")]
    [InlineData(new[] { "Hard" }, "Normal")] // gap in the chain (shouldn't happen live) → still gated
    public async Task GetAvailableQuests_ReportsHighestUnlockedDifficulty(
        string[] completedTiers, string expected)
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        b.Definitions.Setup(d => d.GetAll()).Returns(TwoQuestChain());
        b.QuestProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestProgress>());

        // Owner 2026-06-12 — the unlock is ZONE-scoped, so the sweep must cover EVERY node of
        // the zone (here q001 AND q002) at each completed tier.
        var rows = new List<PlayerQuestDifficultyProgress>();
        foreach (var t in completedTiers)
            foreach (var id in new[] { "q001", "q002" })
            {
                var row = PlayerQuestDifficultyProgress.Create(playerId, id, Enum.Parse<QuestDifficulty>(t));
                row.RecordCompletion();
                rows.Add(row);
            }
        b.DifficultyProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var result = await b.Service.GetAvailableQuestsAsync(playerId);

        result.Should().ContainSingle(q => q.Id == "q001")
              .Which.HighestUnlockedDifficulty.Should().Be(expected);
    }

    [Fact]
    public async Task GetAvailableQuests_HighestUnlocked_StaysNormal_WhenOnlyOneZoneNodeSwept()
    {
        // Owner 2026-06-12 — q001 alone at Normal does NOT open Hard: q002 (same zone) lacks one.
        var b = BuildService();
        var playerId = Guid.NewGuid();

        b.Definitions.Setup(d => d.GetAll()).Returns(TwoQuestChain());
        b.QuestProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestProgress>());
        var only = PlayerQuestDifficultyProgress.Create(playerId, "q001", QuestDifficulty.Normal);
        only.RecordCompletion();
        b.DifficultyProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestDifficultyProgress> { only });

        var result = await b.Service.GetAvailableQuestsAsync(playerId);

        result.Should().ContainSingle(q => q.Id == "q001")
              .Which.HighestUnlockedDifficulty.Should().Be("Normal");
    }

    [Fact]
    public async Task GetAvailableQuests_ZeroCompletionRows_DoNotUnlock()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        b.Definitions.Setup(d => d.GetAll()).Returns(TwoQuestChain());
        b.QuestProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestProgress>());
        // A row exists (e.g. created mid-attempt) but with zero completions — still locked.
        b.DifficultyProgress.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestDifficultyProgress>
            {
                PlayerQuestDifficultyProgress.Create(playerId, "q001", QuestDifficulty.Normal),
            });

        var result = await b.Service.GetAvailableQuestsAsync(playerId);

        result.Should().ContainSingle(q => q.Id == "q001")
              .Which.HighestUnlockedDifficulty.Should().Be("Normal");
    }

    // AttemptQuestAsync — Normal difficulty success

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
        // Owner 2026-06-15 — quest XP is DETERMINISTIC 1.5/energy (Dawn-faithful steady leveling). q001
        // costs 5 energy (Normal/Ch1/Z0) ⇒ round(5 × 1.5) = 8. Authored ExperienceReward(50) is ignored.
        result.ExperienceGranted.Should().Be(8);
        result.CompletionCount.Should().Be(1);
        result.Difficulty.Should().Be("Normal");
        result.DifficultyColor.Should().Be("Green");
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, 5, It.IsAny<CancellationToken>()), Times.Once);
        b.AuditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // AttemptQuestAsync — difficulty energy/reward multipliers

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

        // Unlock the ZONE-scoped difficulty gate (owner 2026-06-12): the gate sweeps every node
        // of the zone via GetAllForPlayerAsync — with a bare GetById fixture the zone is just
        // this node, so completing its prior tiers unlocks the attempt.
        if (difficulty > QuestDifficulty.Normal)
        {
            var gateRows = new List<PlayerQuestDifficultyProgress>();
            for (var gd = QuestDifficulty.Normal; gd < difficulty; gd++)
            {
                var gateProg = PlayerQuestDifficultyProgress.Create(player.Id, "q_multi", gd);
                gateProg.RecordCompletion();
                gateRows.Add(gateProg);
            }
            b.DifficultyProgress.Setup(r => r.GetAllForPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(gateRows);
        }

        int expectedEnergy = (int)Math.Ceiling(10 * energyMult);
        int expectedGold   = (int)(100 * rewardMult);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_multi", difficulty);

        result.Success.Should().BeTrue();
        result.DifficultyColor.Should().Be(color);
        result.GoldGranted.Should().Be(expectedGold);
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, expectedEnergy, It.IsAny<CancellationToken>()), Times.Once);
    }

    // AttemptQuestAsync — node depletion (System 20)

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

    // AttemptQuestAsync — difficulty gate enforcement

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

    [Fact]
    public async Task AttemptQuest_ZoneDifficultyGate_Locked_WhenASiblingLacksPriorTierCompletion()
    {
        // Owner 2026-06-12 — the gate is ZONE-scoped: q001 has its Normal completion, but its
        // zone sibling q002 does not, so Hard stays locked on EVERY node of the zone.
        var b = BuildService();
        var player = MakePlayer();
        var defs = TwoQuestChain();
        b.Definitions.Setup(d => d.GetById("q001")).Returns(defs[0]);
        b.Definitions.Setup(d => d.GetAll()).Returns(defs);

        var q1Normal = PlayerQuestDifficultyProgress.Create(player.Id, "q001", QuestDifficulty.Normal);
        q1Normal.RecordCompletion();
        b.DifficultyProgress.Setup(r => r.GetAllForPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerQuestDifficultyProgress> { q1Normal });

        var result = await b.Service.AttemptQuestAsync(player.Id, "q001", QuestDifficulty.Hard);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.DifficultyLocked);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttemptQuest_ZoneDifficultyGate_Unlocks_WhenWholeZoneSweptAtPriorTier()
    {
        var b = BuildService();
        var player = MakePlayer();
        var defs = TwoQuestChain();
        b.Definitions.Setup(d => d.GetById("q001")).Returns(defs[0]);
        b.Definitions.Setup(d => d.GetAll()).Returns(defs);
        SetupPlayerAndEnergy(b, player);

        var rows = new List<PlayerQuestDifficultyProgress>();
        foreach (var id in new[] { "q001", "q002" })
        {
            var prog = PlayerQuestDifficultyProgress.Create(player.Id, id, QuestDifficulty.Normal);
            prog.RecordCompletion();
            rows.Add(prog);
        }
        b.DifficultyProgress.Setup(r => r.GetAllForPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q001", QuestDifficulty.Hard);

        result.Success.Should().BeTrue("every node of the zone has a Normal completion, so Hard is open");
    }

    [Fact]
    public async Task AttemptQuest_SigilDoesNotDrop_FromANonFinalBossNode()
    {
        // Owner 2026-06-12 — sigils drop ONLY from the zone's FINAL boss node. A boss with a
        // later sibling in its zone (NodeIndex above it) must never drop one, even first-clear.
        var b = BuildService();
        var player = MakePlayer();
        var boss = BossQuest();                       // NodeIndex 1
        var after = new QuestDefinition
        {
            Id = "q_after", Name = "After Boss", Chapter = 1, ZoneIndex = 0, ZoneName = "Z0",
            NodeIndex = 2, BaseEnergyCost = 5, GoldReward = 50, ExperienceReward = 25,
        };
        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(boss);
        b.Definitions.Setup(d => d.GetAll()).Returns(new List<QuestDefinition> { boss, after });
        SetupPlayerAndEnergy(b, player);

        // The zone-boss attempt gate needs every NON-boss sibling ever-cleared.
        var afterProg = PlayerQuestProgress.Create(player.Id, "q_after");
        afterProg.Deplete(100);
        b.QuestProgress.Setup(r => r.GetAsync(player.Id, "q_after", It.IsAny<CancellationToken>()))
            .ReturnsAsync(afterProg);

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_boss", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().NotContain(i => i.ItemId.StartsWith("sigil_"),
            "only the zone's final boss node may drop sigils");
    }

    // AttemptQuestAsync — insufficient energy (no side effects)

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

    // AttemptQuestAsync — prerequisite not met

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

    // AttemptQuestAsync — quest not found

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

    // AttemptQuestAsync — level-up wiring (AddExperience with milestone formula)
    // XpToNextLevel is mocked to return 1000 (from BuildService default).

    [Fact]
    public async Task AttemptQuest_ExactLevelUp_AtThreshold_GrantsLevelUpPoints()
    {
        // Player has 950 XP toward level 2. Quest grants 50 XP. 950+50=1000 = exactly one level.
        // XP = energy(5) × roll, pinned to 10/energy (min==max) ⇒ exactly 50. Level-independent.
        var b = BuildService(questConfig: PinnedXp(10)); // XpToNextLevel → 1000
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
        // XP = energy(5) × 10/energy (pinned) = 50.
        var b = BuildService(questConfig: PinnedXp(10)); // XpToNextLevel → 1000
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
        // XP = energy(5) × 12/energy (pinned) = 60.
        var b = BuildService(questConfig: PinnedXp(12));
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
        // XP = energy(10) × 250/energy (pinned) = 2500.
        var b = BuildService(questConfig: PinnedXp(250)); // XpToNextLevel → 1000
        var player = MakePlayer(); // Level=1, XP=0

        var quest = new QuestDefinition
        {
            Id = "xp_quest", Name = "XP Quest", Chapter = 1, ZoneIndex = 0, NodeType = "Boss",
            BaseEnergyCost = 10, GoldReward = 0, ExperienceReward = 1250, GemReward = 0,
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

    // AttemptQuestAsync — sigil drop (Boss node first completion = guaranteed)

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

    // System 22 Phase A Slice 7 — Discernment drop-quality (rarity-upgrade)

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

    // System 22 Phase A Slice 6 — Hoard (gold) + Discernment (sigil-find)

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
    public async Task AttemptQuest_RerunSigil_IsFlatRate_NotScaledByDiscernment()
    {
        // System 25 — the rerun sigil chance is a FLAT config rate, no longer scaled by Discernment.
        // Pin the rate to 0 and apply a large sigil-find modifier: a rerun must STILL drop nothing.
        var b = BuildService(questConfig: new QuestConfig { SigilRerunDropChance = 0.0 });
        var player = MakePlayer();
        var boss = new QuestDefinition
        {
            Id = "q_boss", Name = "Boss Quest", Chapter = 1, BaseEnergyCost = 8,
            NodeType = "Boss", GoldReward = 200, ExperienceReward = 100,
            Sigils = new Dictionary<string, string> { ["Normal"] = "sigil_ironcolossus_normal" },
        };
        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(boss);
        SetupPlayerAndEnergy(b, player);

        var priorDiff = PlayerQuestDifficultyProgress.Create(player.Id, "q_boss", QuestDifficulty.Normal);
        priorDiff.RecordCompletion();
        priorDiff.MarkSigilDropped(); // first-clear sigil already taken → exercises the rerun path
        b.DifficultyProgress.Setup(r => r.GetAsync(player.Id, "q_boss", QuestDifficulty.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorDiff);
        b.ItemDefs.Setup(d => d.GetById("sigil_ironcolossus_normal")).Returns(new ItemDefinition
        {
            Id = "sigil_ironcolossus_normal", Name = "Iron Sigil", Rarity = ItemRarity.Green, Type = ItemType.Sigil,
        });
        // A large Discernment sigil-find modifier must NOT resurrect the drop (decoupled in System 25).
        b.Mastery.Setup(m => m.GetLootModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryLootModifiers(1.0, 1.0, 5.0, 0.0));

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_boss", QuestDifficulty.Normal);

        result.Success.Should().BeTrue();
        result.ItemsGranted.Should().NotContain(i => i.ItemId == "sigil_ironcolossus_normal",
            "rerun sigil chance is a flat config rate (0 here) — Discernment no longer raises it");
    }

    [Fact]
    public async Task AttemptQuest_BossNode_DropsSigil_OnRerun_AtFlatConfigRate()
    {
        // System 25 — rerun drop is governed by QuestConfig.SigilRerunDropChance, not the per-boss JSON
        // value. Pin it to 1.0 → always drops regardless of RNG seed.
        var b = BuildService(questConfig: new QuestConfig { SigilRerunDropChance = 1.0 });
        var player = MakePlayer();
        var boss = new QuestDefinition
        {
            Id = "q_boss", Name = "Boss Quest", Chapter = 1, BaseEnergyCost = 8,
            NodeType = "Boss", GoldReward = 200, ExperienceReward = 100,
            Sigils = new Dictionary<string, string> { ["Normal"] = "sigil_ironcolossus_normal" },
        };

        b.Definitions.Setup(d => d.GetById("q_boss")).Returns(boss);
        SetupPlayerAndEnergy(b, player);

        // First sigil already dropped → exercises the rerun path
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
            "SigilRerunDropChance=1.0 means the rerun sigil always drops");
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

    // AttemptQuestAsync — loot table items granted

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

    // AttemptQuestAsync — gem idempotency key includes difficulty

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

    // G2: QuestService gear drop wiring

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

    // AttemptQuestAsync — Discernment-scaled chance drops (System 20 Slice 2)

    // Returns a fixed roll from NextDouble() so chance thresholds are deterministic.
    private sealed class FixedRandom : Random
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public override double NextDouble() => _value;
    }

    private static (ServiceBundle b, Player player) GearDropFixture(
        double roll, int discernment, bool rare = false, double baseChance = 0.10)
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

        var lootTable = new LootTableDefinition
        {
            Id = "lt_disc", Type = "Quest",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new()
                {
                    GearDrops = new List<GearDropChance>
                    {
                        new() { GearDefinitionId = "gear_pano_helm", Quantity = 1, Chance = baseChance, RareScaling = rare },
                    },
                },
            },
        };
        b.LootTables.Setup(l => l.GetById("lt_disc")).Returns(lootTable);
        b.Stats.Setup(s => s.GetStatsAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerStatsResponse { DiscernmentInvestment = discernment });

        return (b, player);
    }

    // Owner 2026-06-12 — chase-set ("rare") drop curve:
    //   chance = base + RareDropMaxBonus(0.045) × d / (d + 50,000), hard-capped at base + bonus.
    //   Pano base 0.005 → 0.5% at 0 Disc, ~3.5% at 100k Disc, asymptote 5%.

    [Fact]
    public async Task AttemptQuest_RareGearDrop_StaysAtBase_WithZeroDiscernment()
    {
        // base 0.005, Disc 0 → 0.005; roll 0.03 ≥ 0.005 → no drop.
        var (b, player) = GearDropFixture(roll: 0.03, discernment: 0, rare: true, baseChance: 0.005);

        await b.Service.AttemptQuestAsync(player.Id, "q_disc", QuestDifficulty.Normal);

        b.Equipment.Verify(e => e.GrantGearAsync(
            It.IsAny<Guid>(), "gear_pano_helm", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttemptQuest_RareGearDrop_ReachesMidCurve_At100kDiscernment()
    {
        // base 0.005, Disc 100k → 0.005 + 0.045 × (100k / 150k) = 0.035; roll 0.03 < 0.035 → drops.
        var (b, player) = GearDropFixture(roll: 0.03, discernment: 100_000, rare: true, baseChance: 0.005);

        await b.Service.AttemptQuestAsync(player.Id, "q_disc", QuestDifficulty.Normal);

        b.Equipment.Verify(e => e.GrantGearAsync(
            player.Id, "gear_pano_helm", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttemptQuest_RareGearDrop_NeverExceedsTheCap_EvenAtExtremeDiscernment()
    {
        // base 0.005, Disc 10M → asymptote ≈ 0.005 + 0.045 = 0.05 cap (the generic multiplier
        // would have blown this to 95%); roll 0.06 ≥ 0.05 → still no drop.
        var (b, player) = GearDropFixture(roll: 0.06, discernment: 10_000_000, rare: true, baseChance: 0.005);

        await b.Service.AttemptQuestAsync(player.Id, "q_disc", QuestDifficulty.Normal);

        b.Equipment.Verify(e => e.GrantGearAsync(
            It.IsAny<Guid>(), "gear_pano_helm", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
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

    // Owner 2026-06-14 — XP scales with ENERGY SPENT, not authored XP, not node type, not player level.
    // XP = summed roll(min..max) per energy spent. Energy carries chapter/zone/difficulty scaling, so XP
    // inherits all of it (a boss earns more only because it costs more energy). Pinning min==max collapses
    // the roll to a constant (PinnedXp), giving exact assertions: XP = energyCost × perEnergy.

    private static QuestDefinition XpNode(
        int chapter, int zoneIndex, string nodeType = "Battle", int experienceReward = 100)
        => new()
        {
            Id = "q_xp", Name = "XP Node", Chapter = chapter, ZoneIndex = zoneIndex, ZoneName = "Z",
            NodeIndex = 0, NodeType = nodeType, BaseEnergyCost = 5,
            GoldReward = 0, ExperienceReward = experienceReward, GemReward = 0,
        };

    [Theory]
    // baseEnergy 5, pinned 2 XP/energy ⇒ XP = energyCost × 2. energyCost = ceil(5 × diffMult ×
    // chapterEnergyMult × (1 + zoneIndex×0.04)). Chapter/zone/difficulty raise XP only via energy; a Boss
    // earns the same as a Battle when it costs the same energy (NodeType no longer affects XP).
    [InlineData(1, 0, "Battle", QuestDifficulty.Normal,     5, 10)]  // 5 × 1.0 × 1.00 × 1.00
    [InlineData(1, 0, "Boss",   QuestDifficulty.Normal,     5, 10)]  // boss == battle at equal energy
    [InlineData(1, 2, "Battle", QuestDifficulty.Normal,     6, 12)]  // zone depth: 5 × 1.08 = 5.4 → ceil 6
    [InlineData(2, 0, "Battle", QuestDifficulty.Normal,     6, 12)]  // chapter 2: 5 × 1.11 = 5.55 → ceil 6
    [InlineData(1, 0, "Battle", QuestDifficulty.Nightmare, 15, 30)]  // difficulty ×3 energy ⇒ ×3 XP
    [InlineData(1, 0, "Boss",   QuestDifficulty.Nightmare, 15, 30)]  // boss == battle at equal energy
    public async Task AttemptQuest_XpScalesWithEnergySpent_NotNodeType(
        int chapter, int zoneIndex, string nodeType, QuestDifficulty difficulty,
        int expectedEnergy, int expectedXp)
    {
        var b = BuildService(questConfig: PinnedXp(2));
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q_xp")).Returns(XpNode(chapter, zoneIndex, nodeType));
        SetupPlayerAndEnergy(b, player);

        // Unlock the ZONE-scoped difficulty gate (single-node zone with a bare GetById fixture).
        if (difficulty > QuestDifficulty.Normal)
        {
            var gateRows = new List<PlayerQuestDifficultyProgress>();
            for (var gd = QuestDifficulty.Normal; gd < difficulty; gd++)
            {
                var gateProg = PlayerQuestDifficultyProgress.Create(player.Id, "q_xp", gd);
                gateProg.RecordCompletion();
                gateRows.Add(gateProg);
            }
            b.DifficultyProgress.Setup(r => r.GetAllForPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(gateRows);
        }

        var result = await b.Service.AttemptQuestAsync(player.Id, "q_xp", difficulty);

        result.Success.Should().BeTrue();
        result.ExperienceGranted.Should().Be(expectedXp);
        result.XpGained.Should().Be(expectedXp);
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Energy, expectedEnergy,
            It.IsAny<CancellationToken>()), Times.Once, "XP tracks the energy actually spent");
    }

    [Fact]
    public async Task AttemptQuest_XpIgnoresAuthoredExperienceReward()
    {
        // Two nodes, identical energy (5), wildly different authored ExperienceReward ⇒ identical XP.
        var b = BuildService(questConfig: PinnedXp(3));
        var player = MakePlayer();
        b.Definitions.Setup(d => d.GetById("q_lo")).Returns(
            new QuestDefinition { Id = "q_lo", Name = "Lo", Chapter = 1, ZoneIndex = 0, ZoneName = "Z",
                NodeIndex = 0, NodeType = "Battle", BaseEnergyCost = 5, ExperienceReward = 1 });
        b.Definitions.Setup(d => d.GetById("q_hi")).Returns(
            new QuestDefinition { Id = "q_hi", Name = "Hi", Chapter = 1, ZoneIndex = 0, ZoneName = "Z",
                NodeIndex = 0, NodeType = "Battle", BaseEnergyCost = 5, ExperienceReward = 999_999 });
        SetupPlayerAndEnergy(b, player);

        var lo = await b.Service.AttemptQuestAsync(player.Id, "q_lo", QuestDifficulty.Normal);
        var hi = await b.Service.AttemptQuestAsync(player.Id, "q_hi", QuestDifficulty.Normal);

        lo.ExperienceGranted.Should().Be(15);   // 5 energy × 3/energy
        hi.ExperienceGranted.Should().Be(15);   // authored 999,999 is ignored
    }

    [Fact]
    public async Task AttemptQuest_XpIsLevelIndependent()
    {
        // Level only raises XpToNextLevel, never the XP earned. A level-1 and a high-level player earn the
        // same XP from the same node (same energy spent).
        var b = BuildService(questConfig: PinnedXp(2));
        b.Definitions.Setup(d => d.GetById("q_xp")).Returns(XpNode(1, 0, "Battle"));

        var low  = MakePlayer();              // Level 1
        var high = MakePlayer(xp: 100_000);   // 100 levels in (MakePlayer levels at 1000 XP each)
        high.Level.Should().BeGreaterThan(50, "sanity: the high player is well past level 1");
        SetupPlayerAndEnergy(b, low);
        SetupPlayerAndEnergy(b, high);

        var lowRes  = await b.Service.AttemptQuestAsync(low.Id,  "q_xp", QuestDifficulty.Normal);
        var highRes = await b.Service.AttemptQuestAsync(high.Id, "q_xp", QuestDifficulty.Normal);

        lowRes.ExperienceGranted.Should().Be(10);    // 5 energy × 2
        highRes.ExperienceGranted.Should().Be(10);   // identical despite the level gap
    }

    // T45 — zone-boss gate (a per-zone boss requires all preceding zone nodes cleared)

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

    [Fact]
    public async Task AttemptQuest_ZoneBoss_ReLocked_AfterReset_UntilSiblingsReClearedThisCycle()
    {
        // System 25 — after a zone reset the siblings keep HasEverCleared but lose IsCleared, so the boss
        // RE-LOCKS until the zone is fully re-run this cycle. Proves the gate uses current-cycle IsCleared,
        // NOT the permanent latch (the old behaviour let the boss through immediately after a reset).
        var b = BuildService();
        var player = MakePlayer();
        var defs = ZoneWithBoss();
        b.Definitions.Setup(d => d.GetById("zb")).Returns(defs[2]);
        b.Definitions.Setup(d => d.GetAll()).Returns(defs);
        SetupPlayerAndEnergy(b, player);

        foreach (var id in new[] { "zn0", "zn1" })
        {
            var p = PlayerQuestProgress.Create(player.Id, id);
            p.Deplete(100); // cleared once → HasEverCleared latched
            p.Reset(100);   // a prior zone reset → IsCleared back to false, HasEverCleared preserved
            p.IsCleared.Should().BeFalse();
            p.HasEverCleared.Should().BeTrue();
            b.QuestProgress.Setup(r => r.GetAsync(player.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        }

        var result = await b.Service.AttemptQuestAsync(player.Id, "zb", QuestDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(QuestFailureCode.ZoneBossLocked);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // T45 — cross-zone ordering (a zone's first node is prereq-locked on the previous zone's boss)

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

    // T45 — availability DTO carries zone fields + greys a boss whose zone isn't depleted

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
