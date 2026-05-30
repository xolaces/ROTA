using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class RaidServiceTests
{
    // -----------------------------------------------------------------------
    // FIXTURES
    // -----------------------------------------------------------------------

    private record ServiceBundle(
        RaidService Service,
        Mock<IActiveRaidRepository> Raids,
        Mock<IRaidParticipantRepository> Participants,
        Mock<IPlayerRepository> Players,
        Mock<IPlayerResourceRepository> Resources,
        Mock<IEnergyService> Energy,
        Mock<IGemService> Gems,
        Mock<IStatService> Stats,
        Mock<IPlayerInventoryRepository> Inventory,
        Mock<IItemDefinitionProvider> ItemDefs,
        Mock<ILootTableProvider> LootTables,
        Mock<IAuditLogRepository> AuditLog,
        Mock<IRaidDefinitionProvider> Definitions,
        Mock<IRaidHitCache> HitCache);

    private static ServiceBundle BuildService(Random? random = null)
    {
        var raids        = new Mock<IActiveRaidRepository>();
        var participants = new Mock<IRaidParticipantRepository>();
        var players      = new Mock<IPlayerRepository>();
        var resources    = new Mock<IPlayerResourceRepository>();
        var energy       = new Mock<IEnergyService>();
        var gems         = new Mock<IGemService>();
        var stats        = new Mock<IStatService>();
        var inventory    = new Mock<IPlayerInventoryRepository>();
        var itemDefs     = new Mock<IItemDefinitionProvider>();
        var lootTables   = new Mock<ILootTableProvider>();
        var auditLog     = new Mock<IAuditLogRepository>();
        var definitions  = new Mock<IRaidDefinitionProvider>();
        var hitCache     = new Mock<IRaidHitCache>();

        hitCache.Setup(c => c.TryAcquireSlotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (RaidHitResponse?)null));
        hitCache.Setup(c => c.StoreResultAsync(It.IsAny<string>(), It.IsAny<RaidHitResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stats.Setup(s => s.GrantLevelUpPointsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stats.Setup(s => s.AddUnassignedPointsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: 1000 XP per level — keeps existing kill-reward tests from triggering extra level-ups
        stats.Setup(s => s.XpToNextLevel(It.IsAny<int>())).Returns(1000);

        var service = new RaidService(
            raids.Object, participants.Object, players.Object, resources.Object,
            energy.Object, gems.Object, stats.Object, inventory.Object,
            itemDefs.Object, lootTables.Object, auditLog.Object,
            definitions.Object, hitCache.Object, random);

        return new ServiceBundle(service, raids, participants, players, resources, energy, gems,
            stats, inventory, itemDefs, lootTables, auditLog, definitions, hitCache);
    }

    private static Player MakePlayer(long xp = 0)
    {
        var p = Player.Create("testuser", "test@rota.test", "hash");
        if (xp > 0) p.AddExperience(xp, _ => 1000);
        return p;
    }

    private static RaidDefinition IronColossus() => new()
    {
        Id                   = "raid_ironcolossus",
        Name                 = "The Iron Colossus",
        Tier                 = "World",
        BaseHp               = 100000,
        PersonalBaseHp       = 500,
        TimerHours           = 48,
        StaminaCostPerHit    = 1,
        LootTableId          = "lt_raid_ironcolossus",
        BaseGoldReward       = 500,
        BaseExperienceReward = 300,
        BaseGemReward        = 2,
        HasOnHitDrops        = false,
    };

    private static ActiveRaid MakeRaid(long currentHp = 100000, bool isDefeated = false, int hoursUntilExpiry = 48, RaidDifficulty difficulty = RaidDifficulty.Normal)
    {
        var raid = ActiveRaid.Create(
            "raid_ironcolossus", Guid.NewGuid(), 100000,
            DateTimeOffset.UtcNow.AddHours(hoursUntilExpiry), difficulty);

        if (currentHp < 100000)
            raid.TakeDamage(100000 - currentHp);
        if (isDefeated)
            raid.MarkDefeated();

        return raid;
    }

    private static PlayerResource MakeStaminaResource(int maxValue = 5)
        => PlayerResource.Create(Guid.NewGuid(), ResourceType.Stamina, maxValue, regenPerMinute: 1);

    private static void SetupHitScaffolding(ServiceBundle b, Player player, ActiveRaid raid, bool staminaSuccess = true)
    {
        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        // AtomicApplyHitAsync: in unit tests there is no real DB transaction — just invoke the
        // delegate directly against the raid object so service logic is fully exercised.
        b.Raids.Setup(r => r.AtomicApplyHitAsync(
                raid.Id,
                It.IsAny<Func<ActiveRaid, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<ActiveRaid, Task<bool>>, CancellationToken>(
                (_, mutate, _) => mutate(raid));
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staminaSuccess);
        b.Players.Setup(p => p.FindByIdWithStatsAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        b.Resources.Setup(r => r.GetAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStaminaResource());
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
    }

    // -----------------------------------------------------------------------
    // SummonRaidAsync — difficulty HP multipliers
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RaidDifficulty.Normal,    100000,  "Green")]
    [InlineData(RaidDifficulty.Hard,      140000,  "Yellow")]
    [InlineData(RaidDifficulty.Legendary, 200000,  "Red")]
    [InlineData(RaidDifficulty.Nightmare, 360000,  "Purple")]
    public async Task Summon_AppliesHpMultiplier_PerDifficulty(RaidDifficulty difficulty, long expectedHp, string expectedColor)
    {
        var b = BuildService();
        var player = MakePlayer();

        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Service.SummonRaidAsync(player.Id, "raid_ironcolossus", difficulty);

        result.Success.Should().BeTrue();
        result.Response!.MaxHp.Should().Be(expectedHp);
        result.Response.DifficultyColor.Should().Be(expectedColor);
        b.Raids.Verify(r => r.CreateAsync(
            It.Is<ActiveRaid>(a => a.MaxHp == expectedHp && a.Difficulty == difficulty),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — stamina cost per hit size
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task Hit_SpendsCorrectStamina_PerHitSize(int hitSize)
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, hitSize, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, hitSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — damage formula
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ComputesDamage_FromPlayerStats_InExpectedRange()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        // base = ATK*4+DEF = 10*4+10=50, hitSize=1, RNG[0.85,1.15] → damage in [42,57]
        result.Success.Should().BeTrue();
        result.Response!.DamageDealt.Should().BeInRange(42, 58);
        result.Response.DamageDealt.Should().BeGreaterOrEqualTo(1);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsCachedResponse_OnDuplicateKey_ZeroReprocessing()
    {
        var b = BuildService();
        var key = Guid.NewGuid().ToString();
        var cached = new RaidHitResponse { Success = true, DamageDealt = 42 };

        // TryAcquireSlotAsync returns (false, response) → duplicate found, return immediately.
        b.HitCache.Setup(c => c.TryAcquireSlotAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, cached));

        var raid = MakeRaid();
        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(Guid.NewGuid(), raid.Id, 1, key);

        result.Success.Should().BeTrue();
        result.Response!.DamageDealt.Should().Be(42);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Participants.Verify(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Raids.Verify(r => r.AtomicApplyHitAsync(It.IsAny<Guid>(), It.IsAny<Func<ActiveRaid, Task<bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — expired
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsExpired_WhenTimerElapsed_StaminaUntouched()
    {
        var b = BuildService();
        var expiredRaid = MakeRaid(hoursUntilExpiry: -1);

        b.Raids.Setup(r => r.FindByIdAsync(expiredRaid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(expiredRaid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(Guid.NewGuid(), expiredRaid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.RaidExpired);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — already defeated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsAlreadyDefeated_WhenRaidDead_StaminaUntouched()
    {
        var b = BuildService();
        var defeatedRaid = MakeRaid(isDefeated: true);

        b.Raids.Setup(r => r.FindByIdAsync(defeatedRaid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(defeatedRaid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(Guid.NewGuid(), defeatedRaid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.RaidAlreadyDefeated);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — insufficient stamina
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsInsufficientStamina_HpUnchanged()
    {
        var b = BuildService();
        var player = MakePlayer();
        var raid = MakeRaid();
        var hpBefore = raid.CurrentHp;

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.InsufficientStamina);
        raid.CurrentHp.Should().Be(hpBefore);
        b.Participants.Verify(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Kill rewards — tier multipliers applied correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_AppliesTierMultiplier_ToGoldAndXp_ForLegendary1()
    {
        // Legendary1 tier multiplier = 1.50×; Normal HP multiplier = 1.0×
        // Gold = 500 * 1.0 * 1.50 = 750; XP = 300 * 1.0 * 1.50 = 450
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var bystander = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        SetupHitScaffolding(b, attacker, raid);
        b.Players.Setup(p => p.FindByIdAsync(bystander.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bystander);

        var attackerPart = RaidParticipant.Create(raid.Id, attacker.Id);
        for (int i = 0; i < 10; i++) attackerPart.RecordHit(10000);
        var bystanderPart = RaidParticipant.Create(raid.Id, bystander.Id);
        bystanderPart.RecordHit(100);

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attackerPart);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { attackerPart, bystanderPart });
        b.Players.Setup(p => p.FindByIdAsync(bystander.Id, It.IsAny<CancellationToken>())).ReturnsAsync(bystander);
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.Rewards.Should().NotBeNull();
        result.Response.Rewards!.ContributionTier.Should().Be("Legendary");
        result.Response.Rewards.TierMultiplier.Should().Be(1.50m);
        result.Response.Rewards.GoldGranted.Should().Be(750);
        result.Response.Rewards.ExperienceGranted.Should().Be(450);
    }

    // -----------------------------------------------------------------------
    // Kill rewards — participant gets no gems
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_ParticipantTierReceivesNoGems()
    {
        // 4 players: positions 0/1/2 = Legendary1/2/3; position 3 falls to minContributionPercent check.
        // p4 has 1 damage out of ~27000+ total → ~0.003% << 0.1% default → Participant → no gems.
        var b = BuildService(new Random(0));

        var p1 = MakePlayer(); var p2 = MakePlayer();
        var p3 = MakePlayer(); var p4 = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Raids.Setup(r => r.AtomicApplyHitAsync(
                raid.Id,
                It.IsAny<Func<ActiveRaid, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<ActiveRaid, Task<bool>>, CancellationToken>(
                (_, mutate, _) => mutate(raid));
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(p1.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        b.Players.Setup(p => p.FindByIdWithStatsAsync(p1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p1);
        b.Players.Setup(p => p.FindByIdAsync(p2.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p2);
        b.Players.Setup(p => p.FindByIdAsync(p3.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p3);
        b.Players.Setup(p => p.FindByIdAsync(p4.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p4);
        b.Players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        b.Resources.Setup(r => r.GetAsync(p1.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStaminaResource());
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(p1.Id, ResourceType.Stamina, It.IsAny<CancellationToken>())).ReturnsAsync(4);

        // p1=Legendary1 (top), p2=Legendary2, p3=Legendary3, p4=Participant (<0.1%)
        var p1part = RaidParticipant.Create(raid.Id, p1.Id);
        for (int i = 0; i < 10; i++) p1part.RecordHit(10000); // 100000 dmg
        var p2part = RaidParticipant.Create(raid.Id, p2.Id);
        for (int i = 0; i < 9; i++) p2part.RecordHit(1000);  // 9000 dmg
        var p3part = RaidParticipant.Create(raid.Id, p3.Id);
        for (int i = 0; i < 8; i++) p3part.RecordHit(1000);  // 8000 dmg
        var p4part = RaidParticipant.Create(raid.Id, p4.Id);
        p4part.RecordHit(1);                                  // 1 dmg → ~0.0008% << 0.1% → Participant

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, p1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1part);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { p1part, p2part, p3part, p4part });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.HitRaidAsync(p1.Id, raid.Id, 1, Guid.NewGuid().ToString());

        b.Gems.Verify(g => g.GrantGemsAsync(p4.Id, It.IsAny<int>(), GemTransactionType.RaidReward, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
            "p4 has <0.1% contribution so falls to Participant tier which receives no gems");
    }

    // -----------------------------------------------------------------------
    // Kill rewards — cumulative threshold loot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_AwardsCumulativeThresholdRewards_WhenPlayerQualifiesMultipleTiers()
    {
        // Player at 30% contribution qualifies for both 0.1% and 5% and 20% thresholds
        // Total unassigned SP = 1 + 2 + 3 = 6 (each tier cumulative)
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        var lootTable = new LootTableDefinition
        {
            Id = "lt_raid_ironcolossus", Type = "Raid",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new()
                {
                    MinContributionPercent = 0.1,
                    ThresholdRewards = new List<ThresholdReward>
                    {
                        new() { ContributionPercent = 0.1,  UnassignedStatPoints = 1, ItemDrops = new() },
                        new() { ContributionPercent = 5.0,  UnassignedStatPoints = 2, ItemDrops = new() },
                        new() { ContributionPercent = 20.0, UnassignedStatPoints = 3, ItemDrops = new() },
                    },
                },
            },
        };
        b.LootTables.Setup(l => l.GetById("lt_raid_ironcolossus")).Returns(lootTable);

        // Set up attacker with 30000 / 100000 = 30% contribution
        var attackerPart = RaidParticipant.Create(raid.Id, attacker.Id);
        for (int i = 0; i < 3; i++) attackerPart.RecordHit(10000);

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Raids.Setup(r => r.AtomicApplyHitAsync(
                raid.Id,
                It.IsAny<Func<ActiveRaid, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<ActiveRaid, Task<bool>>, CancellationToken>(
                (_, mutate, _) => mutate(raid));
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(attacker.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        b.Players.Setup(p => p.FindByIdWithStatsAsync(attacker.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attacker);
        b.Players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        b.Resources.Setup(r => r.GetAsync(attacker.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStaminaResource());
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(attacker.Id, ResourceType.Stamina, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attackerPart);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { attackerPart });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // Tier multiplier for Legendary1 = 1.5, so 6 SP * 1.5 = 9 (rounded)
        result.Response!.Rewards!.UnassignedStatPointsGranted.Should().Be(9,
            "cumulative thresholds at 0.1%, 5%, and 20% each grant stat points, scaled by Legendary1 multiplier");
        b.Stats.Verify(s => s.AddUnassignedPointsAsync(attacker.Id, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Kill — gem idempotency key format
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_UsesCorrectGemIdempotencyKeyFormat()
    {
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        SetupHitScaffolding(b, attacker, raid);

        var attackerPart = RaidParticipant.Create(raid.Id, attacker.Id);
        attackerPart.RecordHit(1000);

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attackerPart);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { attackerPart });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        var expectedRef = $"raid:{raid.Id}:{attacker.Id}";
        b.Gems.Verify(g => g.GrantGemsAsync(
            attacker.Id, It.IsAny<int>(), GemTransactionType.RaidReward, expectedRef,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // GetActiveRaidsAsync — filtering and damage tracking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetActiveRaids_ExcludesDefeatedAndExpiredRaids()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        b.Raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveRaid>());
        b.Definitions.Setup(d => d.GetById(It.IsAny<string>())).Returns((RaidDefinition?)null);

        var result = await b.Service.GetActiveRaidsAsync(playerId);

        result.Should().BeEmpty();
        b.Raids.Verify(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActiveRaids_PopulatesYourDamage_ForCallingPlayer()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var raid = MakeRaid();

        var participation = RaidParticipant.Create(raid.Id, playerId);
        participation.RecordHit(12400);

        b.Raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveRaid> { raid });
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participation);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.GetActiveRaidsAsync(playerId);

        result.Should().HaveCount(1);
        result[0].YourTotalDamage.Should().Be(12400);
        result[0].YourHitCount.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // RaidSize — Personal raid summon uses PersonalBaseHp
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RaidDifficulty.Normal,    500)]
    [InlineData(RaidDifficulty.Hard,      700)]
    [InlineData(RaidDifficulty.Legendary, 1000)]
    [InlineData(RaidDifficulty.Nightmare, 1800)]
    public async Task Summon_PersonalRaid_UsesPersonalBaseHp(RaidDifficulty difficulty, long expectedHp)
    {
        var b = BuildService();
        var player = MakePlayer();

        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus()); // PersonalBaseHp = 500
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Service.SummonRaidAsync(player.Id, "raid_ironcolossus", difficulty, RaidSize.Personal);

        result.Success.Should().BeTrue();
        result.Response!.MaxHp.Should().Be(expectedHp,
            $"Personal raid HP = PersonalBaseHp({500}) × difficultyMultiplier");
        result.Response.Size.Should().Be("Personal");
        b.Raids.Verify(r => r.CreateAsync(
            It.Is<ActiveRaid>(a => a.MaxHp == expectedHp && a.Size == RaidSize.Personal),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Summon_LargeRaid_StillUsesBaseHp()
    {
        var b = BuildService();
        var player = MakePlayer();

        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Service.SummonRaidAsync(player.Id, "raid_ironcolossus", RaidDifficulty.Normal, RaidSize.Large);

        result.Success.Should().BeTrue();
        result.Response!.MaxHp.Should().Be(100000, "Large raid ignores PersonalBaseHp and uses BaseHp");
        result.Response.Size.Should().Be("Large");
    }

    // -----------------------------------------------------------------------
    // RaidSize — Personal raid hit access gate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_PersonalRaid_BySummoner_Succeeds()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = ActiveRaid.Create(
            "raid_ironcolossus", player.Id, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant>());

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue("the summoner is always allowed to hit their own personal raid");
    }

    [Fact]
    public async Task Hit_PersonalRaid_ByNonSummoner_ReturnsAccessDenied()
    {
        var b = BuildService(new Random(0));
        var summonerId = Guid.NewGuid();
        var attacker   = MakePlayer();   // different player

        var raid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.AccessDenied,
            "a non-summoner is blocked before any stamina is spent");
        b.Energy.Verify(
            e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "access gate fires before the stamina-spend step");
    }

    // -----------------------------------------------------------------------
    // Resource split lock — energy=quest / stamina=raid (regression guard)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RaidHit_SpendsSamina_NotEnergy_ResourceTypeLocked()
    {
        // Guard: a raid hit must call SpendEnergyAsync with ResourceType.Stamina only.
        // Energy (quest resource) must never be touched by the raid path.
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        b.Energy.Verify(e => e.SpendEnergyAsync(
            player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once, "raid path must spend Stamina");
        b.Energy.Verify(e => e.SpendEnergyAsync(
            player.Id, ResourceType.Energy, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "raid path must never touch Energy (quest resource)");
    }

    // -----------------------------------------------------------------------
    // RaidSize — Personal raid visibility in GetActiveRaidsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetActiveRaids_PersonalRaid_VisibleOnlyToSummoner()
    {
        var b = BuildService();
        var summonerId  = Guid.NewGuid();
        var otherPlayer = Guid.NewGuid();

        var personalRaid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);
        var largeRaid = MakeRaid(); // RaidSize.Large — visible to all

        b.Raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveRaid> { personalRaid, largeRaid });
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        // Non-summoner: only sees the Large world raid
        var otherResult = await b.Service.GetActiveRaidsAsync(otherPlayer);
        otherResult.Should().HaveCount(1, "Personal raid is hidden from non-summoner");
        otherResult[0].Size.Should().Be("Large");

        // Summoner: sees both their Personal raid and the Large raid
        var summonerResult = await b.Service.GetActiveRaidsAsync(summonerId);
        summonerResult.Should().HaveCount(2, "Summoner can see their own Personal raid alongside Large raids");
        summonerResult.Should().ContainSingle(r => r.Size == "Personal");
        summonerResult.Should().ContainSingle(r => r.Size == "Large");
    }
}
