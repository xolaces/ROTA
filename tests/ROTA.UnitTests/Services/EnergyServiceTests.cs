using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class EnergyServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds the service with a known class rate: minutesPerPoint for Energy/Stamina/GuildStamina
    /// all set to <paramref name="minutesPerPoint"/> (default 0.5 = 2 points/min).
    /// </summary>
    private static (EnergyService service,
                    Mock<IPlayerResourceRepository> resources,
                    Mock<IAuditLogRepository> auditLog,
                    Mock<IPlayerRepository> players,
                    Mock<IClassService> classService)
        BuildService(double energyMinutesPerPoint = 0.5, double staminaMinutesPerPoint = 0.5,
                     double guildStaminaMinutesPerPoint = 2.0, PlayerClass playerClass = PlayerClass.Conscript)
    {
        var resources    = new Mock<IPlayerResourceRepository>();
        var auditLog     = new Mock<IAuditLogRepository>();
        var players      = new Mock<IPlayerRepository>();
        var classService = new Mock<IClassService>();

        var regenRates = new ClassRegenRates
        {
            CurrentClass                       = playerClass.ToString(),
            EnergyRegenMinutesPerPoint         = energyMinutesPerPoint,
            StaminaRegenMinutesPerPoint        = staminaMinutesPerPoint,
            GuildStaminaRegenMinutesPerPoint   = guildStaminaMinutesPerPoint,
            IsConverged                        = false,
        };

        classService.Setup(c => c.GetRegenRates(It.IsAny<PlayerClass>())).Returns(regenRates);

        var player = Player.Create("testuser", "test@test.com", "hash");
        if (playerClass != PlayerClass.Conscript)
            player.SetClass(playerClass);
        players.Setup(p => p.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        var service = new EnergyService(resources.Object, auditLog.Object, players.Object, classService.Object);
        return (service, resources, auditLog, players, classService);
    }

    /// <summary>Creates a resource using the public factory — private setters are set inside the domain.</summary>
    private static PlayerResource MakeResource(
        int currentValue,
        int maxValue,
        int regenPerMinute,
        DateTimeOffset? lastRegenAt = null)
        => PlayerResource.Create(
            Guid.NewGuid(),
            ResourceType.Energy,
            maxValue,
            regenPerMinute);

    /// <summary>
    /// Builds a resource and then simulates a checkpoint at a past time by spending
    /// energy to a specific value. Used to control CurrentValue in tests.
    /// </summary>
    private static PlayerResource MakeResourceWithCheckpoint(
        int currentValue,
        int maxValue,
        int regenPerMinute,
        DateTimeOffset lastRegenAt)
    {
        var r = PlayerResource.Create(Guid.NewGuid(), ResourceType.Energy, maxValue, regenPerMinute);
        r.SaveCheckpoint(currentValue, lastRegenAt);
        return r;
    }

    // -----------------------------------------------------------------------
    // GetCurrentEnergyAsync — regen from class config (not stored RegenPerMinute)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetCurrentEnergy_ComputesRegenFromClassRate_NotStoredRegenPerMinute()
    {
        // Class rate: 0.5 min/point = 2 points/min.
        // Stored RegenPerMinute = 0 — must be IGNORED.
        // 10 minutes elapsed, checkpoint = 0 → live = floor(10 / 0.5) = 20.
        var (service, resources, _, players, classService) = BuildService(energyMinutesPerPoint: 0.5);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-10);
        var resource = MakeResourceWithCheckpoint(0, 100, regenPerMinute: 0, lastRegen);

        resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.Energy);

        result.Should().Be(20, "10 minutes at 0.5 min/point = 20 points regenerated from class config");
    }

    [Fact]
    public async Task GetCurrentEnergy_CapsAtMaxValue()
    {
        var (service, resources, _, players, _) = BuildService(energyMinutesPerPoint: 0.5);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-100); // would regenerate 200, capped at 50
        var resource = MakeResourceWithCheckpoint(0, 50, regenPerMinute: 0, lastRegen);

        resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.Energy);

        result.Should().Be(50, "live value must be capped at MaxValue");
    }

    [Fact]
    public async Task GetCurrentEnergy_ZeroRegen_ReturnsSameCheckpointValue()
    {
        // minutesPerPoint = 0 means no regen — value stays at checkpoint.
        var (service, resources, _, players, _) = BuildService(energyMinutesPerPoint: 0);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-60);
        var resource = MakeResourceWithCheckpoint(3, 10, regenPerMinute: 0, lastRegen);

        resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.Energy);

        result.Should().Be(3, "minutesPerPoint=0 means no regen, value stays at checkpoint");
    }

    // -----------------------------------------------------------------------
    // GuildStamina — now regenerates from class config (behavior change v0.2.2)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetCurrentEnergy_GuildStamina_RegeneratesAtClassRate()
    {
        // BEHAVIOR CHANGE (v0.2.2): GuildStamina was previously stored with RegenPerMinute=0.
        // It now regenerates at the class GuildStamina rate (2.0 min/point = 0.5 point/min).
        // After 10 minutes at 2.0 min/point: floor(10 / 2.0) = 5 points regenerated.
        var (service, resources, _, players, _) = BuildService(guildStaminaMinutesPerPoint: 2.0);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-10);
        // Store regenPerMinute=0 (old value) — must be IGNORED in favor of class rate
        var resource = PlayerResource.Create(Guid.NewGuid(), ResourceType.GuildStamina, 20, regenPerMinute: 0);
        resource.SaveCheckpoint(0, lastRegen);

        resources.Setup(r => r.GetAsync(playerId, ResourceType.GuildStamina, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.GuildStamina);

        result.Should().Be(5, "GuildStamina regenerates at class rate 2.0 min/point: floor(10/2.0)=5");
    }

    // -----------------------------------------------------------------------
    // ResourceType → class rate routing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetCurrentEnergy_RoutesEnergyToEnergyRate()
    {
        // Energy rate: 1.0 min/point; Stamina rate: 10.0 min/point (deliberately different).
        // After 5 minutes with Energy: floor(5 / 1.0) = 5 points.
        var (service, resources, _, players, _) = BuildService(
            energyMinutesPerPoint: 1.0, staminaMinutesPerPoint: 10.0);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-5);
        var resource = MakeResourceWithCheckpoint(0, 100, regenPerMinute: 0, lastRegen);

        resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.Energy);

        result.Should().Be(5, "Energy rate (1.0 min/pt) used: floor(5/1.0)=5");
    }

    [Fact]
    public async Task GetCurrentEnergy_RoutesStaminaToStaminaRate()
    {
        // Stamina rate: 2.0 min/point; Energy rate: 0.1 min/point (deliberately different).
        // After 10 minutes with Stamina: floor(10 / 2.0) = 5 points.
        var (service, resources, _, players, _) = BuildService(
            energyMinutesPerPoint: 0.1, staminaMinutesPerPoint: 2.0);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-10);
        var resource = PlayerResource.Create(Guid.NewGuid(), ResourceType.Stamina, 100, regenPerMinute: 0);
        resource.SaveCheckpoint(0, lastRegen);

        resources.Setup(r => r.GetAsync(playerId, ResourceType.Stamina, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.Stamina);

        result.Should().Be(5, "Stamina rate (2.0 min/pt) used: floor(10/2.0)=5");
    }

    // -----------------------------------------------------------------------
    // SpendEnergyAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpendEnergy_Succeeds_WithSufficientEnergy()
    {
        var (service, resources, auditLog, players, _) = BuildService(energyMinutesPerPoint: 0);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        // Resource with 20 energy; spending 5 should succeed.
        var resource = MakeResourceWithCheckpoint(20, 50, 0, DateTimeOffset.UtcNow);

        resources.Setup(r => r.AtomicUpdateAsync(
                    playerId,
                    ResourceType.Energy,
                    It.IsAny<Func<PlayerResource, bool>>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Guid _, ResourceType _, Func<PlayerResource, bool> fn, CancellationToken _)
                     => fn(resource));

        var result = await service.SpendEnergyAsync(playerId, ResourceType.Energy, 5);

        result.Should().BeTrue("player has enough energy");
        auditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SpendEnergy_Fails_WithInsufficientEnergy()
    {
        var (service, resources, auditLog, players, _) = BuildService(energyMinutesPerPoint: 0);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        // Resource with 2 energy; spending 5 must fail.
        var resource = MakeResourceWithCheckpoint(2, 50, 0, DateTimeOffset.UtcNow);

        resources.Setup(r => r.AtomicUpdateAsync(
                    playerId,
                    ResourceType.Energy,
                    It.IsAny<Func<PlayerResource, bool>>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Guid _, ResourceType _, Func<PlayerResource, bool> fn, CancellationToken _)
                     => fn(resource));

        var result = await service.SpendEnergyAsync(playerId, ResourceType.Energy, 5);

        result.Should().BeFalse("player has insufficient energy");
        auditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never,
            "audit log must not record a failed spend");
    }

    [Fact]
    public async Task SpendEnergy_Guard_PreventsDoubleDeduct()
    {
        // Simulates the race condition guard: the updateFn is called with the live value
        // at lock acquisition time. If energy was already spent, the live value will be
        // below the requested amount.
        var (service, resources, _, players, _) = BuildService(energyMinutesPerPoint: 0);
        var playerId = Guid.NewGuid();

        var knownPlayer = Player.Create("u", "u@t.com", "h");
        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(knownPlayer);

        // After first spend: checkpoint is 0 (spent all 3). Second spend of 3 must fail.
        var alreadySpentResource = MakeResourceWithCheckpoint(0, 10, 0, DateTimeOffset.UtcNow);

        resources.Setup(r => r.AtomicUpdateAsync(
                    playerId,
                    ResourceType.Energy,
                    It.IsAny<Func<PlayerResource, bool>>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Guid _, ResourceType _, Func<PlayerResource, bool> fn, CancellationToken _)
                     => fn(alreadySpentResource));

        var result = await service.SpendEnergyAsync(playerId, ResourceType.Energy, 3);

        result.Should().BeFalse(
            "the row-level lock ensures the second concurrent spend sees the updated checkpoint and is rejected");
    }
}
