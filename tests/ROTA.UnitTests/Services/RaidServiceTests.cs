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

    private static (RaidService service,
                    Mock<IActiveRaidRepository> raids,
                    Mock<IRaidParticipantRepository> participants,
                    Mock<IPlayerRepository> players,
                    Mock<IPlayerResourceRepository> resources,
                    Mock<IEnergyService> energy,
                    Mock<IGemService> gems,
                    Mock<IAuditLogRepository> auditLog,
                    Mock<IRaidDefinitionProvider> definitions,
                    Mock<IRaidHitCache> hitCache)
        BuildService(Random? random = null)
    {
        var raids        = new Mock<IActiveRaidRepository>();
        var participants = new Mock<IRaidParticipantRepository>();
        var players      = new Mock<IPlayerRepository>();
        var resources    = new Mock<IPlayerResourceRepository>();
        var energy       = new Mock<IEnergyService>();
        var gems         = new Mock<IGemService>();
        var auditLog     = new Mock<IAuditLogRepository>();
        var definitions  = new Mock<IRaidDefinitionProvider>();
        var hitCache     = new Mock<IRaidHitCache>();

        // Cache miss by default so every test starts fresh
        hitCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidHitResponse?)null);
        hitCache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<RaidHitResponse>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var service = new RaidService(
            raids.Object, participants.Object, players.Object, resources.Object,
            energy.Object, gems.Object, auditLog.Object, definitions.Object,
            hitCache.Object, random);

        return (service, raids, participants, players, resources, energy, gems, auditLog, definitions, hitCache);
    }

    private static Player MakePlayer(long xp = 0)
    {
        var p = Player.Create("testuser", "test@rota.test", "hash");
        if (xp > 0) p.AddExperience(xp);
        return p;
    }

    private static RaidDefinition IronColossus() => new()
    {
        Id                   = "raid_ironcolossus",
        Name                 = "The Iron Colossus",
        Tier                 = "World",
        HpPool               = 100000,
        TimerHours           = 48,
        StaminaCostPerHit    = 1,
        GoldReward           = 500,
        ExperienceReward     = 300,
        GemReward            = 2,
        MinContributionForLoot = 500,
    };

    private static ActiveRaid MakeRaid(long currentHp = 100000, bool isDefeated = false, int hoursUntilExpiry = 48)
    {
        var raid = ActiveRaid.Create(
            "raid_ironcolossus",
            Guid.NewGuid(),
            100000,
            DateTimeOffset.UtcNow.AddHours(hoursUntilExpiry));

        // Simulate reduced HP by applying damage
        if (currentHp < 100000)
            raid.TakeDamage(100000 - currentHp);

        if (isDefeated)
            raid.MarkDefeated();

        return raid;
    }

    private static PlayerResource MakeStaminaResource(int maxValue = 5)
    {
        return PlayerResource.Create(Guid.NewGuid(), ResourceType.Stamina, maxValue, regenPerMinute: 1);
    }

    private static void SetupSuccessfulStaminaSpend(Mock<IEnergyService> energy, Guid playerId)
        => energy.Setup(e => e.SpendEnergyAsync(playerId, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

    // -----------------------------------------------------------------------
    // SummonRaidAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Summon_CreatesRaid_WithCorrectHpAndExpiry()
    {
        var (service, raids, _, players, _, _, _, auditLog, definitions, _) = BuildService();
        var player = MakePlayer();
        var definition = IronColossus();

        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(definition);
        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var before = DateTimeOffset.UtcNow;
        var result = await service.SummonRaidAsync(player.Id, "raid_ironcolossus");
        var after = DateTimeOffset.UtcNow;

        result.Success.Should().BeTrue();
        result.Response!.MaxHp.Should().Be(100000);
        result.Response.Name.Should().Be("The Iron Colossus");
        result.Response.ExpiresAt.Should().BeCloseTo(before.AddHours(48), TimeSpan.FromSeconds(5));
        result.Response.TimerRemainingSeconds.Should().BeGreaterThan(0);

        raids.Verify(r => r.CreateAsync(
            It.Is<ActiveRaid>(a => a.MaxHp == 100000 && a.CurrentHp == 100000 && !a.IsDefeated),
            It.IsAny<CancellationToken>()), Times.Once);
        auditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
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
        var (service, raids, participants, players, resources, energy, _, _, definitions, _) =
            BuildService(new Random(0));

        var player = MakePlayer();
        var raid = MakeRaid();

        raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        SetupSuccessfulStaminaSpend(energy, player.Id);
        players.Setup(p => p.FindByIdWithStatsAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RaidParticipant?)null);
        participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        raids.Setup(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        resources.Setup(r => r.GetAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(MakeStaminaResource());
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(5 - hitSize);

        var result = await service.HitRaidAsync(player.Id, raid.Id, hitSize, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // staminaCostPerHit = 1, so cost = hitSize × 1
        energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, hitSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — damage formula
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ComputesDamage_FromPlayerStats()
    {
        // RNG returns 0 → multiplier = 0.85
        // BaseAttack=10, BaseDefense=10 → base = 10*4+10 = 50
        // hitSize=1, multiplier=0.85 → damage = floor(50 * 1 * 0.85) = 42
        var (service, raids, participants, players, resources, energy, _, _, definitions, _) =
            BuildService(new Random(0)); // seed 0 gives a fixed sequence

        var player = MakePlayer();
        var raid = MakeRaid();

        raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        SetupSuccessfulStaminaSpend(energy, player.Id);
        players.Setup(p => p.FindByIdWithStatsAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RaidParticipant?)null);
        participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        raids.Setup(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        resources.Setup(r => r.GetAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(MakeStaminaResource());
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(4);

        var result = await service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // base = 10*4+10 = 50, hitSize=1, multiplier in [0.85,1.15] → damage in [42,57]
        result.Response!.DamageDealt.Should().BeInRange(42, 58);
        result.Response.DamageDealt.Should().BeGreaterOrEqualTo(1, "minimum 1 damage enforced");
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsCachedResponse_OnDuplicateIdempotencyKey_ZeroReprocessing()
    {
        var (service, raids, participants, players, resources, energy, _, _, definitions, hitCache) =
            BuildService();

        var idempotencyKey = Guid.NewGuid().ToString();
        var cachedResponse = new RaidHitResponse
        {
            Success = true, DamageDealt = 42, CurrentHp = 99958, MaxHp = 100000,
            HpPercent = 99.958, IsDefeated = false, YourTotalDamage = 42,
            YourHitCount = 1, ParticipantCount = 1, NewStaminaValue = 4, NewStaminaMax = 5,
        };

        // Cache returns a hit for this key
        hitCache.Setup(c => c.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(cachedResponse);

        // We still need to load the raid to validate state (not defeated/expired) before checking cache
        var raid = MakeRaid();
        raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await service.HitRaidAsync(Guid.NewGuid(), raid.Id, 1, idempotencyKey);

        result.Success.Should().BeTrue();
        result.Response!.DamageDealt.Should().Be(42);

        // Zero additional processing — stamina untouched, no DB writes
        energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        participants.Verify(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
        raids.Verify(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — expired raid
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsExpired_WhenTimerElapsed_StaminaUntouched()
    {
        var (service, raids, _, _, _, energy, _, _, definitions, _) = BuildService();

        var expiredRaid = MakeRaid(hoursUntilExpiry: -1); // already expired
        raids.Setup(r => r.FindByIdAsync(expiredRaid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(expiredRaid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await service.HitRaidAsync(Guid.NewGuid(), expiredRaid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.RaidExpired);
        energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — already defeated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsAlreadyDefeated_WhenRaidDead_StaminaUntouched()
    {
        var (service, raids, _, _, _, energy, _, _, definitions, _) = BuildService();

        var defeatedRaid = MakeRaid(isDefeated: true);
        raids.Setup(r => r.FindByIdAsync(defeatedRaid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(defeatedRaid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await service.HitRaidAsync(Guid.NewGuid(), defeatedRaid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.RaidAlreadyDefeated);
        energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — insufficient stamina
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsInsufficientStamina_HpUnchanged()
    {
        var (service, raids, participants, players, _, energy, _, _, definitions, _) = BuildService();
        var player = MakePlayer();
        var raid = MakeRaid();
        var hpBefore = raid.CurrentHp;

        raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var result = await service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.InsufficientStamina);
        raid.CurrentHp.Should().Be(hpBefore, "HP must not change if stamina spend failed");
        participants.Verify(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — kill distributes rewards to all participants
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_KillDistributesGoldAndXp_ToAllParticipants()
    {
        var (service, raids, participants, players, resources, energy, gems, auditLog, definitions, _) =
            BuildService(new Random(0));

        var attacker = MakePlayer();
        var bystander = MakePlayer();
        // Raid with 1 HP so this hit kills it
        var raid = MakeRaid(currentHp: 1);

        raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        SetupSuccessfulStaminaSpend(energy, attacker.Id);
        players.Setup(p => p.FindByIdWithStatsAsync(attacker.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attacker);

        // Two participants: attacker (100k damage) and bystander (100 damage)
        var attackerParticipant = RaidParticipant.Create(raid.Id, attacker.Id);
        for (int i = 0; i < 10; i++) attackerParticipant.RecordHit(10000);

        var bystanderParticipant = RaidParticipant.Create(raid.Id, bystander.Id);
        bystanderParticipant.RecordHit(100);

        participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(attackerParticipant);
        participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
        participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<RaidParticipant> { attackerParticipant, bystanderParticipant });
        raids.Setup(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        players.Setup(p => p.FindByIdAsync(bystander.Id, It.IsAny<CancellationToken>())).ReturnsAsync(bystander);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        resources.Setup(r => r.GetAsync(attacker.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(MakeStaminaResource());
        energy.Setup(e => e.GetCurrentEnergyAsync(attacker.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(4);

        var result = await service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsDefeated.Should().BeTrue();
        result.Response.Rewards.Should().NotBeNull("kill must populate rewards");
        result.Response.Rewards!.GoldGranted.Should().Be(500);
        result.Response.Rewards.ExperienceGranted.Should().Be(300);

        // Both participants updated
        players.Verify(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "RaidKill"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — contribution tiers
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_AssignsContributionTiersCorrectly()
    {
        var (service, raids, participants, players, resources, energy, gems, _, definitions, _) =
            BuildService(new Random(0));

        var definition = IronColossus(); // minContributionForLoot = 500

        // 5 participants: p1=top3 Legendary, p2=top3 Legendary, p3=top3 Legendary, p4=Epic, p5=Participant
        var p1 = MakePlayer(); var p1part = RaidParticipant.Create(Guid.NewGuid(), p1.Id);
        for (int i = 0; i < 10; i++) p1part.RecordHit(10000); // 100000 damage

        var p2 = MakePlayer(); var p2part = RaidParticipant.Create(Guid.NewGuid(), p2.Id);
        for (int i = 0; i < 10; i++) p2part.RecordHit(5000); // 50000 damage

        var p3 = MakePlayer(); var p3part = RaidParticipant.Create(Guid.NewGuid(), p3.Id);
        for (int i = 0; i < 10; i++) p3part.RecordHit(1000); // 10000 damage — Legendary (top 3)

        var p4 = MakePlayer(); var p4part = RaidParticipant.Create(Guid.NewGuid(), p4.Id);
        p4part.RecordHit(600); // 600 >= minContributionForLoot=500 → Rare

        var p5 = MakePlayer(); var p5part = RaidParticipant.Create(Guid.NewGuid(), p5.Id);
        p5part.RecordHit(10); // 10 < 500 → Participant

        var allParts = new List<RaidParticipant> { p1part, p2part, p3part, p4part, p5part };
        // Set the same activeRaidId
        // (Use p1 as the attacker who delivers the killing blow)
        var raid = MakeRaid(currentHp: 1);
        foreach (var part in allParts)
        {
            var field = typeof(RaidParticipant).GetField("<ActiveRaidId>k__BackingField",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(part, raid.Id);
        }

        raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(definition);
        SetupSuccessfulStaminaSpend(energy, p1.Id);
        players.Setup(p => p.FindByIdWithStatsAsync(p1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p1);

        // p1's existing participation (already has 100k damage recorded)
        participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, p1.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(p1part);
        participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
        participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(allParts);
        raids.Setup(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        foreach (var (p, part) in new[] { (p2, p2part), (p3, p3part), (p4, p4part), (p5, p5part) })
            players.Setup(pl => pl.FindByIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);

        players.Setup(pl => pl.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        resources.Setup(r => r.GetAsync(p1.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(MakeStaminaResource());
        energy.Setup(e => e.GetCurrentEnergyAsync(p1.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(4);

        var result = await service.HitRaidAsync(p1.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.Rewards!.ContributionTier.Should().Be("Legendary", "p1 is the top damage dealer");

        // p1, p2, p3 = Legendary → gems granted
        // p4 = Rare (damage >= 500) → gems granted
        // p5 = Participant → no gems
        gems.Verify(g => g.GrantGemsAsync(p1.Id, It.IsAny<int>(), GemTransactionType.RaidReward, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        gems.Verify(g => g.GrantGemsAsync(p4.Id, It.IsAny<int>(), GemTransactionType.RaidReward, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        gems.Verify(g => g.GrantGemsAsync(p5.Id, It.IsAny<int>(), GemTransactionType.RaidReward, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — kill gem idempotency key format
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_UsesCorrectGemIdempotencyKeyFormat()
    {
        var (service, raids, participants, players, resources, energy, gems, _, definitions, _) =
            BuildService(new Random(0));

        var attacker = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        SetupSuccessfulStaminaSpend(energy, attacker.Id);
        players.Setup(p => p.FindByIdWithStatsAsync(attacker.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attacker);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Pre-existing participant so GetAllForRaidAsync can return them for kill processing
        var attackerPart = RaidParticipant.Create(raid.Id, attacker.Id);
        attackerPart.RecordHit(1000); // already dealt some damage

        participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(attackerPart);
        participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
        participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<RaidParticipant> { attackerPart });
        raids.Setup(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        resources.Setup(r => r.GetAsync(attacker.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(MakeStaminaResource());
        energy.Setup(e => e.GetCurrentEnergyAsync(attacker.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(4);
        gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        var expectedRef = $"raid:{raid.Id}:{attacker.Id}";
        gems.Verify(g => g.GrantGemsAsync(
            attacker.Id, It.IsAny<int>(), GemTransactionType.RaidReward, expectedRef,
            It.IsAny<CancellationToken>()), Times.Once,
            $"gem referenceId must be 'raid:{{activeRaidId}}:{{playerId}}' — expected '{expectedRef}'");
    }

    // -----------------------------------------------------------------------
    // GetActiveRaidsAsync — filtering
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetActiveRaids_ExcludesDefeatedAndExpiredRaids()
    {
        // Repository already filters these — just verify GetAllActiveAsync is called
        // and the result is passed through.
        var (service, raids, participants, _, _, _, _, _, definitions, _) = BuildService();
        var playerId = Guid.NewGuid();

        raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<ActiveRaid>()); // empty = all filtered by repository

        definitions.Setup(d => d.GetById(It.IsAny<string>())).Returns((RaidDefinition?)null);

        var result = await service.GetActiveRaidsAsync(playerId);

        result.Should().BeEmpty("repository already excludes defeated and expired raids");
        raids.Verify(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActiveRaids_PopulatesYourTotalDamage_ForCallingPlayer()
    {
        var (service, raids, participants, _, _, _, _, _, definitions, _) = BuildService();
        var playerId = Guid.NewGuid();
        var raid = MakeRaid();

        // Simulate participation
        var participation = RaidParticipant.Create(raid.Id, playerId);
        participation.RecordHit(12400);

        raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<ActiveRaid> { raid });
        participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, playerId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(participation);
        definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await service.GetActiveRaidsAsync(playerId);

        result.Should().HaveCount(1);
        result[0].YourTotalDamage.Should().Be(12400);
        result[0].YourHitCount.Should().Be(1);
    }
}
