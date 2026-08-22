using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class ItemServiceTests
{
    // HELPERS

    private record ServiceBundle(
        ItemService Service,
        Mock<IPlayerInventoryRepository> Inventory,
        Mock<IItemDefinitionProvider> ItemDefs,
        Mock<IRaidDefinitionProvider> RaidDefs,
        Mock<IStatService> Stats,
        Mock<IRaidService> Raids,
        Mock<IAuditLogRepository> AuditLog,
        Mock<IEnergyService> Energy,
        Mock<IPlayerResourceRepository> Resources);

    private static ServiceBundle BuildService()
    {
        var inventory = new Mock<IPlayerInventoryRepository>();
        var itemDefs  = new Mock<IItemDefinitionProvider>();
        var raidDefs  = new Mock<IRaidDefinitionProvider>();
        var stats     = new Mock<IStatService>();
        var raids     = new Mock<IRaidService>();
        var auditLog  = new Mock<IAuditLogRepository>();
        var energy    = new Mock<IEnergyService>();
        var resources = new Mock<IPlayerResourceRepository>();

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        inventory.Setup(r => r.UpdateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new ServiceBundle(
            new ItemService(inventory.Object, itemDefs.Object, raidDefs.Object, stats.Object, raids.Object,
                auditLog.Object, new ROTA.UnitTests.TestSupport.PassThroughPlayerMutationLock(),
                energy.Object, resources.Object),
            inventory, itemDefs, raidDefs, stats, raids, auditLog, energy, resources);
    }

    // ── Consumables (D-008) helpers ────────────────────────────────────────────────────────────
    private static ItemDefinition PotionDef(
        string res = "Energy", int amount = 25, bool toMax = false, long gold = 4000) => new()
    {
        Id = "potion_energy_minor", Name = "Minor Energy Draught",
        Type = ItemType.Consumable, Rarity = ItemRarity.Green, ArtKey = "potion_energy_minor",
        RestoreResourceType = res, RestoreAmount = amount, RestoreToMax = toMax, GoldPrice = gold,
    };

    /// <summary>Wires the pool + a live-value sequence (before, after) for the two GetCurrentEnergyAsync reads.</summary>
    private static void SetupPool(
        ServiceBundle b, Guid playerId, ResourceType type, int max, int before, int after)
    {
        b.Resources.Setup(r => r.GetAsync(playerId, type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerResource.Create(playerId, type, max, 0));
        b.Energy.SetupSequence(e => e.GetCurrentEnergyAsync(playerId, type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(before)
            .ReturnsAsync(after);
    }

    private static ItemDefinition StatBagDef(int sp = 5) => new()
    {
        Id = "statbag_minor", Name = "Minor Stat Bag",
        Type = ItemType.StatBag, Rarity = ItemRarity.Green,
        StatPointsOnUse = sp, ArtKey = "statbag_minor",
    };

    private static ItemDefinition SigilDef(string raidId = "raid_ironcolossus", string difficulty = "Normal") => new()
    {
        Id = $"sigil_{raidId}_{difficulty.ToLower()}",
        Name = $"Iron Sigil ({difficulty})",
        Type = ItemType.Sigil, Rarity = ItemRarity.Green,
        SummonRaidId = raidId, SummonDifficulty = difficulty,
        ArtKey = "sigil_ironcolossus",
    };

    private static ItemDefinition MaterialDef() => new()
    {
        Id = "mat_iron_shard", Name = "Iron Shard",
        Type = ItemType.Material, Rarity = ItemRarity.Grey, ArtKey = "mat_iron_shard",
    };

    private static PlayerInventoryItem MakeInvItem(string itemId, int quantity)
        => PlayerInventoryItem.Create(Guid.NewGuid(), itemId, quantity);

    // GetInventoryAsync

    [Fact]
    public async Task GetInventory_ReturnsMappedItems_HydratedFromDefinitions()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        var inv = MakeInvItem("statbag_minor", 3);
        b.Inventory.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem> { inv });
        b.ItemDefs.Setup(d => d.GetById("statbag_minor")).Returns(StatBagDef());

        var result = await b.Service.GetInventoryAsync(playerId);

        result.Should().HaveCount(1);
        result[0].ItemDefinitionId.Should().Be("statbag_minor");
        result[0].Name.Should().Be("Minor Stat Bag");
        result[0].Quantity.Should().Be(3);
        result[0].Rarity.Should().Be("Green");
    }

    [Fact]
    public async Task GetInventory_ExcludesItems_WithZeroQuantity()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        var consumed = MakeInvItem("statbag_minor", 1);
        consumed.ConsumeQuantity(1); // Quantity=0, IsUsed=true

        b.Inventory.Setup(r => r.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem> { consumed });

        var result = await b.Service.GetInventoryAsync(playerId);

        result.Should().BeEmpty("items with Quantity=0 should be filtered out");
    }

    // UseItemAsync — StatBag grants skill points

    [Fact]
    public async Task UseItem_StatBag_GrantsSkillPoints_AndConsumesQuantity()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = StatBagDef(sp: 5);
        var inv = MakeInvItem(def.Id, 2); // have 2, use 1

        b.ItemDefs.Setup(d => d.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);
        b.Stats.Setup(s => s.AddUnassignedPointsAsync(playerId, 5, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 1);

        result.Success.Should().BeTrue();
        result.StatPointsGranted.Should().Be(5);
        result.RemainingQuantity.Should().Be(1);
        result.QuantityConsumed.Should().Be(1);
        b.Stats.Verify(s => s.AddUnassignedPointsAsync(playerId, 5, It.IsAny<CancellationToken>()), Times.Once);
        b.Inventory.Verify(r => r.UpdateAsync(It.Is<PlayerInventoryItem>(i => i.Quantity == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseItem_StatBag_StacksPoints_WhenMultipleUsed()
    {
        // Using 3 minor bags (5 SP each) → 15 SP total
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = StatBagDef(sp: 5);
        var inv = MakeInvItem(def.Id, 3);

        b.ItemDefs.Setup(d => d.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);
        b.Stats.Setup(s => s.AddUnassignedPointsAsync(playerId, 15, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 3);

        result.Success.Should().BeTrue();
        result.StatPointsGranted.Should().Be(15);
        b.Stats.Verify(s => s.AddUnassignedPointsAsync(playerId, 15, It.IsAny<CancellationToken>()), Times.Once);
    }

    // UseItemAsync — Sigil summons raid

    [Fact]
    public async Task UseItem_Sigil_SummonsRaid_AndConsumesSignil()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = SigilDef();
        var inv = MakeInvItem(def.Id, 1);

        b.ItemDefs.Setup(d => d.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);

        var summonResponse = new SummonRaidResponse
        {
            ActiveRaidId = Guid.NewGuid(), Name = "The Iron Colossus",
            MaxHp = 100000, ExpiresAt = DateTimeOffset.UtcNow.AddHours(48),
            Difficulty = "Normal", DifficultyColor = "Green",
        };
        b.Raids.Setup(r => r.SummonRaidAsync(playerId, "raid_ironcolossus", RaidDifficulty.Normal, RaidSize.Personal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummonRaidResult { Success = true, Response = summonResponse });

        var result = await b.Service.UseItemAsync(playerId, def.Id, 1);

        result.Success.Should().BeTrue();
        result.RaidSummoned.Should().NotBeNull();
        result.RaidSummoned!.Name.Should().Be("The Iron Colossus");
        result.RemainingQuantity.Should().Be(0);
        b.Raids.Verify(r => r.SummonRaidAsync(playerId, "raid_ironcolossus", RaidDifficulty.Normal, RaidSize.Personal, It.IsAny<CancellationToken>()), Times.Once);
        b.Inventory.Verify(r => r.UpdateAsync(It.Is<PlayerInventoryItem>(i => i.Quantity == 0 && i.IsUsed), It.IsAny<CancellationToken>()), Times.Once);
    }

    // UseItemAsync — insufficient quantity

    [Fact]
    public async Task UseItem_Returns_InsufficientItems_WhenQuantityTooLow()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = StatBagDef();
        var inv = MakeInvItem(def.Id, 1); // only 1 in inventory

        b.ItemDefs.Setup(d => d.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 5); // request 5

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(UseItemFailureCode.InsufficientItems);
        b.Stats.Verify(s => s.AddUnassignedPointsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // UseItemAsync — negative / zero quantity guard (audit fix: item+SP dup)

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task UseItem_Rejects_NonPositiveQuantity_WithoutGrantingOrMutating(int qty)
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = StatBagDef();
        var inv = MakeInvItem(def.Id, 5);

        b.ItemDefs.Setup(d => d.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);

        var result = await b.Service.UseItemAsync(playerId, def.Id, qty);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(UseItemFailureCode.InsufficientItems);
        // The exploit was: negative quantity ADDS items (Quantity -= -1) and grants negative SP.
        b.Stats.Verify(s => s.AddUnassignedPointsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Inventory.Verify(r => r.UpdateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
        inv.Quantity.Should().Be(5, "inventory must be untouched on a rejected use");
    }

    // UseItemAsync — non-usable item type

    [Fact]
    public async Task UseItem_Returns_ItemNotUsable_ForMaterialType()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = MaterialDef();
        var inv = MakeInvItem(def.Id, 5);

        b.ItemDefs.Setup(d => d.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 1);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(UseItemFailureCode.ItemNotUsable);
        b.Inventory.Verify(r => r.UpdateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // UseItemAsync — item definition not found

    [Fact]
    public async Task UseItem_Returns_ItemNotFound_WhenDefinitionMissing()
    {
        var b = BuildService();

        b.ItemDefs.Setup(d => d.GetById("unknown_item")).Returns((ItemDefinition?)null);

        var result = await b.Service.UseItemAsync(Guid.NewGuid(), "unknown_item", 1);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(UseItemFailureCode.ItemNotFound);
    }

    // ── Consumables (D-008 / northstar §1 escape valve) ────────────────────────────────────────

    [Fact]
    public async Task UseItem_Consumable_RestoresResource_AndReportsWhatLanded()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = PotionDef(amount: 25);
        b.ItemDefs.Setup(p => p.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvItem(def.Id, 3));
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 40, after: 65);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 1);

        result.Success.Should().BeTrue();
        result.ResourceRestored.Should().Be("Energy");
        result.ResourceAmountRestored.Should().Be(25);
        result.ResourceNewValue.Should().Be(65);
        result.ResourceMaxValue.Should().Be(100);
        b.Energy.Verify(e => e.RefillEnergyAsync(playerId, ResourceType.Energy, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseItem_Consumable_MultiplesRestoreAmountByQuantity()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = PotionDef(amount: 25);
        b.ItemDefs.Setup(p => p.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvItem(def.Id, 5));
        SetupPool(b, playerId, ResourceType.Energy, max: 200, before: 0, after: 75);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 3);

        result.Success.Should().BeTrue();
        result.QuantityConsumed.Should().Be(3);
        b.Energy.Verify(e => e.RefillEnergyAsync(playerId, ResourceType.Energy, 75, It.IsAny<CancellationToken>()), Times.Once);
    }

    // A potion burned at a full pool would be silent theft — reject instead, and consume nothing.
    [Fact]
    public async Task UseItem_Consumable_AtFullPool_RejectsAndConsumesNothing()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = PotionDef(amount: 25);
        var inv = MakeInvItem(def.Id, 2);
        b.ItemDefs.Setup(p => p.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 100, after: 100);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 1);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(UseItemFailureCode.ResourceAlreadyFull);
        inv.Quantity.Should().Be(2);
        b.Energy.Verify(e => e.RefillEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Inventory.Verify(r => r.UpdateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Regen since the last checkpoint counts toward "full" — we read the LIVE value, not the stored one.
    [Fact]
    public async Task UseItem_Consumable_UsesLiveValueNotStoredCheckpoint()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = PotionDef(amount: 25);
        b.ItemDefs.Setup(p => p.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvItem(def.Id, 1));
        // Stored checkpoint is max (PlayerResource.Create), but the live read says the pool has been spent down.
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 10, after: 35);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 1);

        result.Success.Should().BeTrue();
        result.ResourceAmountRestored.Should().Be(25);
    }

    [Fact]
    public async Task UseItem_FullRefill_CallsRefillToMax_AndRejectsQuantityAboveOne()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = PotionDef(amount: 0, toMax: true, gold: 0);
        b.ItemDefs.Setup(p => p.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvItem(def.Id, 4));
        SetupPool(b, playerId, ResourceType.Energy, max: 100, before: 20, after: 100);

        var ok = await b.Service.UseItemAsync(playerId, def.Id, 1);
        ok.Success.Should().BeTrue();
        ok.ResourceAmountRestored.Should().Be(80);
        b.Energy.Verify(e => e.RefillToMaxAsync(playerId, ResourceType.Energy, It.IsAny<CancellationToken>()), Times.Once);

        // A second bundle: using 2 at once would waste the extra, so it is refused outright.
        var b2 = BuildService();
        b2.ItemDefs.Setup(p => p.GetById(def.Id)).Returns(def);
        b2.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvItem(def.Id, 4));
        SetupPool(b2, playerId, ResourceType.Energy, max: 100, before: 20, after: 100);

        var rejected = await b2.Service.UseItemAsync(playerId, def.Id, 2);
        rejected.Success.Should().BeFalse();
        rejected.FailureCode.Should().Be(UseItemFailureCode.ItemNotUsable);
        b2.Energy.Verify(e => e.RefillToMaxAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UseItem_Consumable_WithUnparseableResource_IsRejected()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = PotionDef(res: "Mana");   // not a ResourceType
        b.ItemDefs.Setup(p => p.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvItem(def.Id, 1));

        var result = await b.Service.UseItemAsync(playerId, def.Id, 1);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(UseItemFailureCode.ItemNotUsable);
    }

    [Fact]
    public async Task UseItem_Consumable_WithNoPoolRow_IsRejected()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var def = PotionDef(res: "GuildStamina");
        b.ItemDefs.Setup(p => p.GetById(def.Id)).Returns(def);
        b.Inventory.Setup(r => r.GetAsync(playerId, def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvItem(def.Id, 1));
        b.Resources.Setup(r => r.GetAsync(playerId, ResourceType.GuildStamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerResource?)null);

        var result = await b.Service.UseItemAsync(playerId, def.Id, 1);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(UseItemFailureCode.ItemNotUsable);
    }
}
