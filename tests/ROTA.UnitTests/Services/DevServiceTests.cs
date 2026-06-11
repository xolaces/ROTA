using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;
using ROTA.UnitTests.TestSupport;

namespace ROTA.UnitTests.Services;

/// <summary>T75 — dev/admin grant actions (AdminOnly + audited).</summary>
public class DevServiceTests
{
    private sealed class Harness
    {
        public Mock<IPlayerRepository> Players { get; } = new();
        public Mock<IGemService> Gems { get; } = new();
        public Mock<IStatService> Stats { get; } = new();
        public Mock<IEnergyService> Energy { get; } = new();
        public Mock<IItemDefinitionProvider> ItemDefs { get; } = new();
        public Mock<IPlayerInventoryRepository> Inventory { get; } = new();
        public Mock<IAuditLogRepository> Audit { get; } = new();
        public DevService Service { get; }

        public Harness()
        {
            Players.SetupMutatePassThrough();
            Stats.Setup(s => s.XpToNextLevel(It.IsAny<int>())).Returns(1000);
            Service = new DevService(
                Players.Object, Gems.Object, Stats.Object, Energy.Object,
                ItemDefs.Object, Inventory.Object, Audit.Object);
        }

        public Player SeedPlayer()
        {
            var p = Player.Create("devtarget", "devtarget@rota.test", "hash");
            Players.Setup(r => r.FindByIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
            return p;
        }
    }

    [Fact]
    public async Task GrantAsync_GoldGemsSpXp_AllApplied_Audited()
    {
        var h = new Harness();
        var actor = Guid.NewGuid();
        var target = h.SeedPlayer();

        var result = await h.Service.GrantAsync(actor, new DevGrantRequest
        { TargetPlayerId = target.Id, Gold = 500, Gems = 25, SkillPoints = 10, Xp = 100 }, "127.0.0.1");

        result.Success.Should().BeTrue();
        target.Gold.Should().Be(500);
        target.Experience.Should().Be(100);
        h.Gems.Verify(g => g.GrantGemsAsync(target.Id, 25, GemTransactionType.AdminGrant,
            It.Is<string>(r => r.StartsWith("dev:")), It.IsAny<CancellationToken>()), Times.Once);
        h.Stats.Verify(s => s.AddUnassignedPointsAsync(target.Id, 10, It.IsAny<CancellationToken>()), Times.Once);
        h.Audit.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "DevGrant" && l.PlayerId == actor), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantAsync_XpCrossingLevel_FiresLevelUpHooks()
    {
        var h = new Harness();
        var target = h.SeedPlayer();

        var result = await h.Service.GrantAsync(target.Id, new DevGrantRequest { Xp = 2500 }, "127.0.0.1");

        result.Success.Should().BeTrue();
        result.NewLevel.Should().Be(3); // 1000 xp per level (stubbed) → L1→L3 with 500 carry
        h.Stats.Verify(s => s.GrantLevelUpPointsAsync(target.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GrantAsync_NegativeOrEmpty_Rejected()
    {
        var h = new Harness();
        (await h.Service.GrantAsync(Guid.NewGuid(), new DevGrantRequest { Gold = -5 }, "ip"))
            .Success.Should().BeFalse();
        (await h.Service.GrantAsync(Guid.NewGuid(), new DevGrantRequest(), "ip"))
            .Success.Should().BeFalse();
        h.Players.Verify(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantItemAsync_NewItem_CreatesStack_ExistingItem_Increments()
    {
        var h = new Harness();
        var target = h.SeedPlayer();
        h.ItemDefs.Setup(d => d.GetById("itm_test")).Returns(new ItemDefinition { Id = "itm_test", Name = "Test Item" });

        h.Inventory.Setup(i => i.GetAsync(target.Id, "itm_test", It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PlayerInventoryItem?)null);
        var first = await h.Service.GrantItemAsync(target.Id, new DevGrantItemRequest
        { ItemDefinitionId = "itm_test", Quantity = 3 }, "ip");
        first.Success.Should().BeTrue();
        h.Inventory.Verify(i => i.CreateAsync(
            It.Is<PlayerInventoryItem>(x => x.Quantity == 3), It.IsAny<CancellationToken>()), Times.Once);

        var stack = PlayerInventoryItem.Create(target.Id, "itm_test", 3);
        h.Inventory.Setup(i => i.GetAsync(target.Id, "itm_test", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(stack);
        var second = await h.Service.GrantItemAsync(target.Id, new DevGrantItemRequest
        { ItemDefinitionId = "itm_test", Quantity = 2 }, "ip");
        second.Success.Should().BeTrue();
        stack.Quantity.Should().Be(5);
    }

    [Fact]
    public async Task GrantItemAsync_UnknownItem_Rejected()
    {
        var h = new Harness();
        h.ItemDefs.Setup(d => d.GetById("nope")).Returns((ItemDefinition?)null);

        var result = await h.Service.GrantItemAsync(Guid.NewGuid(), new DevGrantItemRequest
        { ItemDefinitionId = "nope" }, "ip");

        result.Success.Should().BeFalse();
        h.Inventory.Verify(i => i.CreateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefillAsync_RefillsAllFourPools()
    {
        var h = new Harness();
        var target = h.SeedPlayer();

        var result = await h.Service.RefillAsync(target.Id, new DevRefillRequest(), "ip");

        result.Success.Should().BeTrue();
        foreach (var type in new[] { ResourceType.Energy, ResourceType.Stamina, ResourceType.GuildStamina, ResourceType.Health })
            h.Energy.Verify(e => e.RefillToMaxAsync(target.Id, type, It.IsAny<CancellationToken>()), Times.Once);
    }
}
