using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class EnergyServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private static (EnergyService service,
                    Mock<IPlayerResourceRepository> resources,
                    Mock<IAuditLogRepository> auditLog)
        BuildService()
    {
        var resources = new Mock<IPlayerResourceRepository>();
        var auditLog  = new Mock<IAuditLogRepository>();
        var service   = new EnergyService(resources.Object, auditLog.Object);
        return (service, resources, auditLog);
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
        // Create() sets CurrentValue = MaxValue and LastRegenAt = UtcNow.
        // We need to override via SaveCheckpoint which is a public domain method.
        var r = PlayerResource.Create(Guid.NewGuid(), ResourceType.Energy, maxValue, regenPerMinute);
        r.SaveCheckpoint(currentValue, lastRegenAt);
        return r;
    }

    // -----------------------------------------------------------------------
    // GetCurrentEnergyAsync — regen computation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetCurrentEnergy_ComputesRegenSinceCheckpoint()
    {
        var (service, resources, _) = BuildService();
        var playerId = Guid.NewGuid();
        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-10); // 10 minutes ago
        // currentValue=0, regenPerMinute=2 → after 10 min = 20; maxValue=100 → live = 20
        var resource = MakeResourceWithCheckpoint(0, 100, 2, lastRegen);

        resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.Energy);

        result.Should().Be(20, "10 minutes × 2 per minute = 20");
    }

    [Fact]
    public async Task GetCurrentEnergy_CapsAtMaxValue()
    {
        var (service, resources, _) = BuildService();
        var playerId = Guid.NewGuid();
        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-100); // way too much regen
        var resource = MakeResourceWithCheckpoint(0, 50, 2, lastRegen); // would be 200, capped at 50

        resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.Energy);

        result.Should().Be(50, "live value must be capped at MaxValue");
    }

    [Fact]
    public async Task GetCurrentEnergy_ZeroRegen_ReturnsSameCheckpointValue()
    {
        var (service, resources, _) = BuildService();
        var playerId = Guid.NewGuid();
        var lastRegen = DateTimeOffset.UtcNow.AddMinutes(-60);
        var resource = MakeResourceWithCheckpoint(3, 10, 0, lastRegen); // regenPerMinute = 0

        resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(resource);

        var result = await service.GetCurrentEnergyAsync(playerId, ResourceType.Energy);

        result.Should().Be(3, "zero regen means value stays at checkpoint");
    }

    // -----------------------------------------------------------------------
    // SpendEnergyAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpendEnergy_Succeeds_WithSufficientEnergy()
    {
        var (service, resources, auditLog) = BuildService();
        var playerId = Guid.NewGuid();
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
        var (service, resources, auditLog) = BuildService();
        var playerId = Guid.NewGuid();
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
        // at lock acquisition time. If energy was already spent (checkpoint updated by a
        // prior transaction), the live value will be below the requested amount.
        var (service, resources, _) = BuildService();
        var playerId = Guid.NewGuid();

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
