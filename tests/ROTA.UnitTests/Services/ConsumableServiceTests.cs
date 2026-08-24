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

// D-008 / D-013 — gem-priced instant refills (the premium tier of the northstar §1 escape valve).
public class ConsumableServiceTests
{
    private record Bundle(
        ConsumableService Service,
        Mock<IEnergyService> Energy,
        Mock<IPlayerResourceRepository> Resources,
        Mock<IGemService> Gems,
        Mock<IAuditLogRepository> AuditLog);

    private static Bundle Build(int energyCost = 20, int windowSeconds = 10)
    {
        var energy    = new Mock<IEnergyService>();
        var resources = new Mock<IPlayerResourceRepository>();
        var gems      = new Mock<IGemService>();
        var auditLog  = new Mock<IAuditLogRepository>();

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        energy.Setup(e => e.RefillToMaxAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        gems.Setup(g => g.GetBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(500L);

        var config = Options.Create(new ConsumableConfig
        {
            InstantRefillGemCost = new Dictionary<string, int>
            {
                ["Energy"]  = energyCost,
                ["Stamina"] = 20,
                // Health and GuildStamina deliberately absent — unpriced pools are not refillable.
            },
            RefillIdempotencyWindowSeconds = windowSeconds,
        });

        var service = new ConsumableService(
            energy.Object, resources.Object, gems.Object,
            new ROTA.UnitTests.TestSupport.PassThroughPlayerMutationLock(),
            auditLog.Object, config);

        return new Bundle(service, energy, resources, gems, auditLog);
    }

    private static void SetupPool(Bundle b, Guid playerId, ResourceType type, int max, int before, int after)
    {
        b.Resources.Setup(r => r.GetAsync(playerId, type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerResource.Create(playerId, type, max, 0));
        b.Energy.SetupSequence(e => e.GetCurrentEnergyAsync(playerId, type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(before)
            .ReturnsAsync(after);
    }

    [Fact]
    public async Task Refill_SpendsGems_FillsPool_AndReportsWhatLanded()
    {
        var b = Build(energyCost: 20);
        var playerId = Guid.NewGuid();
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 30, after: 100);
        b.Gems.Setup(g => g.SpendGemsAsync(playerId, 20, GemTransactionType.EnergyRefill,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.Charged);

        var result = await b.Service.RefillAsync(playerId, ResourceType.Energy);

        result.Success.Should().BeTrue();
        result.GemsSpent.Should().Be(20);
        result.AmountRestored.Should().Be(70);
        result.NewValue.Should().Be(100);
        result.MaxValue.Should().Be(100);
        result.NewGemBalance.Should().Be(500);
        b.Energy.Verify(e => e.RefillToMaxAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Charging for a refill that restores nothing reads as theft — refuse before spending.
    [Fact]
    public async Task Refill_WhenAlreadyFull_RefusesWithoutSpending()
    {
        var b = Build();
        var playerId = Guid.NewGuid();
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 100, after: 100);

        var result = await b.Service.RefillAsync(playerId, ResourceType.Energy);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RefillFailureCode.AlreadyFull);
        b.Gems.Verify(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<GemTransactionType>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Energy.Verify(e => e.RefillToMaxAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // An unpriced pool is not purchasable — this is how GuildStamina stays out of the premium path.
    [Fact]
    public async Task Refill_UnpricedResource_IsNotRefillable()
    {
        var b = Build();
        var result = await b.Service.RefillAsync(Guid.NewGuid(), ResourceType.GuildStamina);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RefillFailureCode.NotRefillable);
        b.Gems.Verify(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<GemTransactionType>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refill_InsufficientGems_FillsNothing()
    {
        var b = Build();
        var playerId = Guid.NewGuid();
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 10, after: 10);
        b.Gems.Setup(g => g.SpendGemsAsync(playerId, 20, GemTransactionType.EnergyRefill,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.InsufficientBalance);

        var result = await b.Service.RefillAsync(playerId, ResourceType.Energy);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RefillFailureCode.InsufficientGems);
        b.Energy.Verify(e => e.RefillToMaxAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // AlreadyProcessed = the charge committed but the fill may not have (crash/retry). Completing the
    // fill is how the player gets what they already paid for — same as the magic/unit/legion shops.
    [Fact]
    public async Task Refill_WhenChargeAlreadyProcessed_StillCompletesTheFill()
    {
        var b = Build();
        var playerId = Guid.NewGuid();
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 40, after: 100);
        b.Gems.Setup(g => g.SpendGemsAsync(playerId, 20, GemTransactionType.EnergyRefill,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.AlreadyProcessed);

        var result = await b.Service.RefillAsync(playerId, ResourceType.Energy);

        result.Success.Should().BeTrue();
        b.Energy.Verify(e => e.RefillToMaxAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()), Times.Once);
    }

    // The referenceId is what makes a cross-request retry idempotent, so its shape is load-bearing.
    [Fact]
    public async Task Refill_UsesATimeBucketedReferenceId()
    {
        var b = Build(windowSeconds: 10);
        var playerId = Guid.NewGuid();
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 0, after: 100);

        string? captured = null;
        b.Gems.Setup(g => g.SpendGemsAsync(playerId, 20, GemTransactionType.EnergyRefill,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, long, GemTransactionType, string?, CancellationToken>((_, _, _, r, _) => captured = r)
            .ReturnsAsync(GemSpendOutcome.Charged);

        await b.Service.RefillAsync(playerId, ResourceType.Energy);

        captured.Should().NotBeNull();
        captured.Should().StartWith($"refill:{playerId}:Energy:");
        var bucket = captured!.Split(':').Last();
        long.TryParse(bucket, out var parsed).Should().BeTrue("the bucket must be numeric");
        parsed.Should().BeCloseTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 10, 2);
    }

    [Fact]
    public async Task Refill_MissingPool_ReturnsResourceNotFound()
    {
        var b = Build();
        var playerId = Guid.NewGuid();
        b.Resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerResource?)null);

        var result = await b.Service.RefillAsync(playerId, ResourceType.Energy);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RefillFailureCode.ResourceNotFound);
    }

    [Fact]
    public async Task GetRefillOptions_ListsOnlyPricedPools_WithAffordabilityAndFullness()
    {
        var b = Build(energyCost: 20);
        var playerId = Guid.NewGuid();

        b.Resources.Setup(r => r.GetAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerResource.Create(playerId, ResourceType.Energy, 100, 0));
        b.Resources.Setup(r => r.GetAsync(playerId, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerResource.Create(playerId, ResourceType.Stamina, 50, 0));
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60);
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(playerId, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);   // already full

        var options = await b.Service.GetRefillOptionsAsync(playerId);

        options.Options.Should().HaveCount(2, "only priced pools are offered — Health/GuildStamina are unpriced here");
        options.PlayerGems.Should().Be(500);

        var energyOpt = options.Options.Single(o => o.ResourceType == "Energy");
        energyOpt.CanRefill.Should().BeTrue();
        energyOpt.CanAfford.Should().BeTrue();
        energyOpt.CurrentValue.Should().Be(60);

        var staminaOpt = options.Options.Single(o => o.ResourceType == "Stamina");
        staminaOpt.CanRefill.Should().BeFalse("a full pool must not be offered as refillable");
    }
}
