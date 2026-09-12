using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class EquipmentServiceTests
{
    // Builds a service whose commander slot is wired explicitly, for the overlap cases.
    private static EquipmentService BuildServiceWithCommander(
        Mock<IPlayerEquipmentRepository> repo,
        Mock<IGearDefinitionProvider> gearDefs,
        Mock<IPlayerGearRepository> gearRepo,
        IPlayerCommanderGearRepository commander)
        => new(repo.Object, gearDefs.Object, new Mock<IAuditLogRepository>().Object,
               new Mock<IPlayerInventoryRepository>().Object, new Mock<IItemDefinitionProvider>().Object,
               gearRepo.Object, new Mock<IAchievementService>().Object, commander);

    // Default: nothing in the commander slot. That slot wears a copy too, so EquipAsync counts it —
    // one copy cannot be worn as both a mount and a commander.
    private static IPlayerCommanderGearRepository NoCommanderGear()
    {
        var m = new Mock<IPlayerCommanderGearRepository>();
        m.Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerCommanderGear?)null);
        return m.Object;
    }

    private static (EquipmentService service,
                    Mock<IPlayerEquipmentRepository> repo,
                    Mock<IGearDefinitionProvider> gearDefs,
                    Mock<IAuditLogRepository> auditLog,
                    Mock<IPlayerInventoryRepository> inventory,
                    Mock<IItemDefinitionProvider> itemDefs,
                    Mock<IPlayerGearRepository> gearRepo)
        BuildService()
    {
        var repo      = new Mock<IPlayerEquipmentRepository>();
        var gearDefs  = new Mock<IGearDefinitionProvider>();
        var auditLog  = new Mock<IAuditLogRepository>();
        var inventory = new Mock<IPlayerInventoryRepository>();
        var itemDefs  = new Mock<IItemDefinitionProvider>();
        var gearRepo  = new Mock<IPlayerGearRepository>();

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.CreateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: empty inventory — no conditional bonuses will fire
        inventory.Setup(i => i.GetAllForPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem>());
        // Default: empty gear bag (ownership)
        gearRepo.Setup(g => g.GetOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGear>());
        // Default: nothing equipped (used by ownership spare-check in EquipAsync)
        repo.Setup(r => r.GetEquippedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment>().AsReadOnly());

        var service = new EquipmentService(
            repo.Object, gearDefs.Object, auditLog.Object, inventory.Object, itemDefs.Object, gearRepo.Object, new Mock<IAchievementService>().Object,
            NoCommanderGear());
        return (service, repo, gearDefs, auditLog, inventory, itemDefs, gearRepo);
    }

    private static GearDefinition HelmDef() => new()
    {
        Id           = "gear_conscript_helm",
        Name         = "Conscript Helm",
        Description  = "A battered iron helm.",
        Rarity       = ItemRarity.Grey,
        Slot         = "Head",
        BonusAttack  = 0,
        BonusDefense = 1,
        ProcChance   = null,
        ProcPercent  = null,
        IconPath     = "icons/gear/conscript_helm.png",
    };

    private static GearDefinition MountDef() => new()
    {
        Id           = "gear_draft_horse",
        Name         = "Draft Horse",
        Description  = "A sturdy workhorse.",
        Rarity       = ItemRarity.Grey,
        Slot         = "Mount",
        BonusAttack  = 0,
        BonusDefense = 0,
        ProcChance   = 0.05,
        ProcPercent  = 2.0,
        IconPath     = "icons/gear/draft_horse.png",
    };

    [Fact]
    public async Task Equip_ValidItem_ReturnsSuccess()
    {
        var (service, repo, gearDefs, _, _, _, gearRepo) = BuildService();
        var playerId = Guid.NewGuid();

        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(HelmDef());
        repo.Setup(r => r.FindBySlotAsync(It.IsAny<Guid>(), EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEquipment?)null);
        gearRepo.Setup(r => r.GetAsync(playerId, "gear_conscript_helm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerGear.Create(playerId, "gear_conscript_helm", 1));

        var result = await service.EquipAsync(playerId, "Head", "gear_conscript_helm");

        result.Success.Should().BeTrue();
        result.Item.Should().NotBeNull();
        result.Item!.Slot.Should().Be("Head");
        result.Item.BonusDefense.Should().Be(1);
        repo.Verify(r => r.CreateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Equip_UnknownSlot_ReturnsFailure()
    {
        var (service, _, gearDefs, _, _, _, _) = BuildService();

        var result = await service.EquipAsync(Guid.NewGuid(), "Shoulder", "gear_conscript_helm");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Unknown slot");
    }

    [Fact]
    public async Task Equip_GearNotFound_ReturnsFailure()
    {
        var (service, _, gearDefs, _, _, _, _) = BuildService();

        gearDefs.Setup(g => g.GetById("nonexistent")).Returns((GearDefinition?)null);

        var result = await service.EquipAsync(Guid.NewGuid(), "Head", "nonexistent");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
    }

    [Fact]
    public async Task Equip_SlotMismatch_ReturnsFailure()
    {
        var (service, _, gearDefs, _, _, _, _) = BuildService();

        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(HelmDef()); // Slot=Head

        var result = await service.EquipAsync(Guid.NewGuid(), "Neck", "gear_conscript_helm");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("belongs to slot");
    }

    // EquipAsync — existing slot swaps gear (UpdateAsync, not CreateAsync)

    [Fact]
    public async Task Equip_ExistingSlot_SwapsGear()
    {
        var (service, repo, gearDefs, _, _, _, gearRepo) = BuildService();
        var playerId = Guid.NewGuid();

        var existingRow = PlayerEquipment.Create(playerId, EquipmentSlot.Head, "gear_old_helm");
        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(HelmDef());
        repo.Setup(r => r.FindBySlotAsync(playerId, EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRow);
        gearRepo.Setup(r => r.GetAsync(playerId, "gear_conscript_helm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerGear.Create(playerId, "gear_conscript_helm", 1));

        var result = await service.EquipAsync(playerId, "Head", "gear_conscript_helm");

        result.Success.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(existingRow, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.CreateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unequip_EquippedSlot_ReturnsSuccess()
    {
        var (service, repo, _, _, _, _, _) = BuildService();

        var existingRow = PlayerEquipment.Create(Guid.NewGuid(), EquipmentSlot.Head, "gear_conscript_helm");
        repo.Setup(r => r.FindBySlotAsync(It.IsAny<Guid>(), EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRow);

        var result = await service.UnequipAsync(Guid.NewGuid(), "Head");

        result.Success.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(existingRow, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unequip_EmptySlot_ReturnsFailure()
    {
        var (service, repo, _, _, _, _, _) = BuildService();

        repo.Setup(r => r.FindBySlotAsync(It.IsAny<Guid>(), EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEquipment?)null);

        var result = await service.UnequipAsync(Guid.NewGuid(), "Head");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("No item equipped");
    }

    [Fact]
    public async Task GetEffectiveCombatData_WithGear_AddsStatBonuses()
    {
        // Two equipped items: helm (DEF+1) + iron ring (ATK+1); base ATK=10, DEF=10
        // Expected: effective ATK=11, DEF=13 (helm + collar + chest each add 1 DEF, but spec says +3 DEF total)
        // Per spec test 8: "BonusAttack=1, BonusDefense=3 total; base ATK=10, DEF=10; effective = ATK=11, DEF=13"
        var (service, repo, gearDefs, _, _, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        var helmRow  = PlayerEquipment.Create(playerId, EquipmentSlot.Head, "gear_conscript_helm");
        var ringRow  = PlayerEquipment.Create(playerId, EquipmentSlot.Ring1, "gear_iron_ring");
        var chestRow = PlayerEquipment.Create(playerId, EquipmentSlot.Torso, "gear_conscript_chest");
        var collarRow = PlayerEquipment.Create(playerId, EquipmentSlot.Neck, "gear_conscript_collar");

        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment> { helmRow, ringRow, chestRow, collarRow });

        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(new GearDefinition
            { Id = "gear_conscript_helm", Slot = "Head", BonusAttack = 0, BonusDefense = 1 });
        gearDefs.Setup(g => g.GetById("gear_iron_ring")).Returns(new GearDefinition
            { Id = "gear_iron_ring", Slot = "Ring1", BonusAttack = 1, BonusDefense = 0 });
        gearDefs.Setup(g => g.GetById("gear_conscript_chest")).Returns(new GearDefinition
            { Id = "gear_conscript_chest", Slot = "Torso", BonusAttack = 0, BonusDefense = 1 });
        gearDefs.Setup(g => g.GetById("gear_conscript_collar")).Returns(new GearDefinition
            { Id = "gear_conscript_collar", Slot = "Neck", BonusAttack = 0, BonusDefense = 1 });

        var result = await service.GetEffectiveCombatDataAsync(playerId, 10, 10);

        result.EffectiveAttack.Should().Be(11, "base 10 + ring bonus 1");
        result.EffectiveDefense.Should().Be(13, "base 10 + helm+chest+collar = +3");
        result.MountProc.Should().BeNull("no mount equipped");
    }

    [Fact]
    public async Task GetEffectiveCombatData_WithMount_ReturnsProcData()
    {
        var (service, repo, gearDefs, _, _, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        var mountRow = PlayerEquipment.Create(playerId, EquipmentSlot.Mount, "gear_draft_horse");

        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment> { mountRow });

        gearDefs.Setup(g => g.GetById("gear_draft_horse")).Returns(MountDef());

        var result = await service.GetEffectiveCombatDataAsync(playerId, 10, 10);

        result.MountProc.Should().NotBeNull("a mount with procChance is equipped");
        result.MountProc!.ProcChance.Should().BeApproximately(0.05, 1e-9);
    }

    // Owner, 2026-09-12: the relics carry procs, so a proc on any worn piece is real. Until then a
    // procChance on a Head or Gloves was dead data.
    [Fact]
    public async Task GetEffectiveCombatData_CollectsEveryWornProc_MountFirst()
    {
        var (service, repo, gearDefs, _, _, _, _) = BuildService();
        var playerId = Guid.NewGuid();
        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment>
            {
                PlayerEquipment.Create(playerId, EquipmentSlot.Ring1, "gear_sovereign_tithe"),
                PlayerEquipment.Create(playerId, EquipmentSlot.Mount, "gear_draft_horse"),
                PlayerEquipment.Create(playerId, EquipmentSlot.Head,  "gear_conscript_helm"),
            });
        gearDefs.Setup(g => g.GetById("gear_draft_horse")).Returns(MountDef());
        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(HelmDef());
        gearDefs.Setup(g => g.GetById("gear_sovereign_tithe")).Returns(new GearDefinition
        {
            Id = "gear_sovereign_tithe", Name = "Sovereign's Tithe-Mark", Rarity = ItemRarity.Orange, Slot = "Ring1",
            BonusAttack = 95, BonusDefense = 55, ProcChance = 0.15, ProcPercent = 2.0,
        });

        var result = await service.GetEffectiveCombatDataAsync(playerId, 10, 10);

        result.GearProcs.Should().HaveCount(2, "the mount and the ring proc; the helm has none");
        result.GearProcs![0].Should().Be(result.MountProc, "the mount comes first, carrying its conditional adjustments");
        result.GearProcs[1].ProcChance.Should().BeApproximately(0.15, 1e-9);
        result.GearProcs[1].ProcPercent.Should().BeApproximately(2.0, 1e-9);
        result.EffectiveAttack.Should().Be(10 + 95 + MountDef().BonusAttack + HelmDef().BonusAttack);
    }

    [Fact]
    public async Task GetEffectiveCombatData_ConditionalFlatAttack_AddsToEffectiveAttack()
    {
        // Gear with a conditional bonus: +1 ATK per 1 owned "item_a" (owns 3 → +3 ATK)
        var (service, repo, gearDefs, _, inventory, itemDefs, _) = BuildService();
        var playerId = Guid.NewGuid();

        var ringRow = PlayerEquipment.Create(playerId, EquipmentSlot.Ring1, "gear_cond_ring");
        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment> { ringRow });

        gearDefs.Setup(g => g.GetById("gear_cond_ring")).Returns(new GearDefinition
        {
            Id           = "gear_cond_ring",
            Slot         = "Ring1",
            BonusAttack  = 0,
            BonusDefense = 0,
            ConditionalBonuses = new List<ConditionalBonus>
            {
                new()
                {
                    ConditionType   = ConditionType.OwnedUnitCount,
                    ConditionTarget = "item_a",
                    PerCount        = 1,
                    BonusType       = BonusType.FlatAttack,
                    BonusAmount     = 1,
                },
            },
        });

        // Player owns 3 of item_a
        var item = PlayerInventoryItem.Create(playerId, "item_a", 3);
        inventory.Setup(i => i.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem> { item });
        itemDefs.Setup(d => d.GetById("item_a")).Returns(new ItemDefinition
            { Id = "item_a", Tags = new List<string> { "material" } });

        var result = await service.GetEffectiveCombatDataAsync(playerId, 10, 10);

        result.EffectiveAttack.Should().Be(13, "base 10 + 3 conditional FlatAttack from 3 owned item_a");
        result.EffectiveDefense.Should().Be(10, "no defense bonus");
    }

    [Fact]
    public async Task GetEffectiveCombatData_ConditionalProcChanceClamped_NeverExceedsOne()
    {
        // Mount base procChance = 0.8; conditional adds 0.5 → 1.3, clamped to 1.0
        var (service, repo, gearDefs, _, inventory, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        var mountRow = PlayerEquipment.Create(playerId, EquipmentSlot.Mount, "gear_cond_mount");
        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment> { mountRow });

        gearDefs.Setup(g => g.GetById("gear_cond_mount")).Returns(new GearDefinition
        {
            Id         = "gear_cond_mount",
            Slot       = "Mount",
            ProcChance = 0.8,
            ProcPercent = 2.0,
            ConditionalBonuses = new List<ConditionalBonus>
            {
                new()
                {
                    ConditionType   = ConditionType.OwnedUnitCount,
                    ConditionTarget = "booster",
                    PerCount        = 1,
                    BonusType       = BonusType.ProcChanceFlat,
                    BonusAmount     = 0.5,
                },
            },
        });

        inventory.Setup(i => i.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem>
            {
                PlayerInventoryItem.Create(playerId, "booster", 1),
            });

        var result = await service.GetEffectiveCombatDataAsync(playerId, 10, 10);

        result.MountProc.Should().NotBeNull();
        result.MountProc!.ProcChance.Should().BeApproximately(1.0, 1e-9,
            "0.8 base + 0.5 conditional = 1.3, clamped to 1.0");
    }

    [Fact]
    public async Task GetEffectiveCombatData_FlatDamagePercent_PropagatesInResult()
    {
        var (service, repo, gearDefs, _, inventory, itemDefs, _) = BuildService();
        var playerId = Guid.NewGuid();

        var ringRow = PlayerEquipment.Create(playerId, EquipmentSlot.Ring1, "gear_dmg_ring");
        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment> { ringRow });

        gearDefs.Setup(g => g.GetById("gear_dmg_ring")).Returns(new GearDefinition
        {
            Id   = "gear_dmg_ring",
            Slot = "Ring1",
            ConditionalBonuses = new List<ConditionalBonus>
            {
                new()
                {
                    ConditionType   = ConditionType.OwnedUnitCount,
                    ConditionTarget = "sigil_item",
                    PerCount        = 5,
                    BonusType       = BonusType.FlatDamagePercent,
                    BonusAmount     = 0.1,
                },
            },
        });

        // Player owns 10 sigil_item → floor(10/5)=2 stacks × 0.1 = 0.2
        inventory.Setup(i => i.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem>
            {
                PlayerInventoryItem.Create(playerId, "sigil_item", 10),
            });
        itemDefs.Setup(d => d.GetById("sigil_item")).Returns(new ItemDefinition
            { Id = "sigil_item", Tags = new List<string>() });

        var result = await service.GetEffectiveCombatDataAsync(playerId, 10, 10);

        result.FlatDamagePercent.Should().BeApproximately(0.2, 1e-9,
            "floor(10/5)=2 stacks × 0.1 = 0.2 FlatDamagePercent");
    }

    [Fact]
    public async Task GetEffectiveCombatData_TagBasedBonus_AccumulatesAcrossItems()
    {
        // Gear grants +2 ATK per 5 items tagged "material"; owns 3 of mat_a + 4 of mat_b = 7 total
        // floor(7/5) = 1 stack × 2 = +2 ATK
        var (service, repo, gearDefs, _, inventory, itemDefs, _) = BuildService();
        var playerId = Guid.NewGuid();

        var ringRow = PlayerEquipment.Create(playerId, EquipmentSlot.Ring1, "gear_tag_ring");
        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment> { ringRow });

        gearDefs.Setup(g => g.GetById("gear_tag_ring")).Returns(new GearDefinition
        {
            Id   = "gear_tag_ring",
            Slot = "Ring1",
            ConditionalBonuses = new List<ConditionalBonus>
            {
                new()
                {
                    ConditionType   = ConditionType.OwnedTypeCount,
                    ConditionTarget = "material",
                    PerCount        = 5,
                    BonusType       = BonusType.FlatAttack,
                    BonusAmount     = 2,
                },
            },
        });

        inventory.Setup(i => i.GetAllForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem>
            {
                PlayerInventoryItem.Create(playerId, "mat_a", 3),
                PlayerInventoryItem.Create(playerId, "mat_b", 4),
            });
        itemDefs.Setup(d => d.GetById("mat_a")).Returns(new ItemDefinition
            { Id = "mat_a", Tags = new List<string> { "material" } });
        itemDefs.Setup(d => d.GetById("mat_b")).Returns(new ItemDefinition
            { Id = "mat_b", Tags = new List<string> { "material" } });

        var result = await service.GetEffectiveCombatDataAsync(playerId, 10, 10);

        result.EffectiveAttack.Should().Be(12,
            "base 10 + floor(7/5)=1 stack × 2 ATK = 12");
    }

    // GetOwnedGearAsync — available = owned − equipped
    private static (EquipmentService service,
                    Mock<IPlayerGearRepository> gearRepo,
                    Mock<IPlayerEquipmentRepository> equipRepo,
                    Mock<IGearDefinitionProvider> gearDefs)
        BuildServiceForOwnedGear()
    {
        var equipRepo = new Mock<IPlayerEquipmentRepository>();
        var gearDefs  = new Mock<IGearDefinitionProvider>();
        var auditLog  = new Mock<IAuditLogRepository>();
        var inventory = new Mock<IPlayerInventoryRepository>();
        var itemDefs  = new Mock<IItemDefinitionProvider>();
        var gearRepo  = new Mock<IPlayerGearRepository>();

        // Default: nothing equipped
        equipRepo.Setup(r => r.GetEquippedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment>());

        var service = new EquipmentService(
            equipRepo.Object, gearDefs.Object, auditLog.Object, inventory.Object, itemDefs.Object, gearRepo.Object, new Mock<IAchievementService>().Object,
            NoCommanderGear());
        return (service, gearRepo, equipRepo, gearDefs);
    }

    // The paper doll reads SetId and ReforgedFrom off the equipped and owned rows: the set to draw
    // the piece from in phase 2, and the mark that says "reforged" today. Both come straight off
    // the definition, and a plain piece carries null for the second.
    [Fact]
    public async Task GetEquipment_And_OwnedGear_CarryTheSetAndTheReforgeLink()
    {
        var (service, gearRepo, equipRepo, gearDefs) = BuildServiceForOwnedGear();
        var playerId = Guid.NewGuid();

        var reforged = HelmDef();
        reforged.Id = "gear_conscript_helm_reforged";
        reforged.SetId = "set_conscript_reforged";
        reforged.ReforgedFrom = "gear_conscript_helm";
        gearDefs.Setup(g => g.GetById("gear_conscript_helm_reforged")).Returns(reforged);
        var plain = HelmDef();
        plain.SetId = "set_conscript";
        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(plain);

        equipRepo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment> { PlayerEquipment.Create(playerId, EquipmentSlot.Head, "gear_conscript_helm_reforged") });
        gearRepo.Setup(g => g.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGear>
            {
                PlayerGear.Create(playerId, "gear_conscript_helm_reforged", 1),
                PlayerGear.Create(playerId, "gear_conscript_helm", 1),
            });

        var worn = (await service.GetEquipmentAsync(playerId)).Single();
        worn.SetId.Should().Be("set_conscript_reforged");
        worn.ReforgedFrom.Should().Be("gear_conscript_helm");

        var owned = await service.GetOwnedGearAsync(playerId);
        owned.Single(g => g.GearDefinitionId == "gear_conscript_helm_reforged").ReforgedFrom.Should().Be("gear_conscript_helm");
        owned.Single(g => g.GearDefinitionId == "gear_conscript_helm").ReforgedFrom.Should().BeNull("an ordinary piece was not made from anything");
        owned.Single(g => g.GearDefinitionId == "gear_conscript_helm").SetId.Should().Be("set_conscript");
    }

    [Fact]
    public async Task GetOwnedGear_NoneEquipped_AvailableEqualsOwned()
    {
        var (service, gearRepo, _, gearDefs) = BuildServiceForOwnedGear();
        var playerId = Guid.NewGuid();

        gearRepo.Setup(g => g.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGear> { PlayerGear.Create(playerId, "gear_conscript_helm", 2) });
        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(HelmDef());

        var owned = await service.GetOwnedGearAsync(playerId);

        owned.Should().HaveCount(1);
        owned[0].GearDefinitionId.Should().Be("gear_conscript_helm");
        owned[0].Slot.Should().Be("Head");
        owned[0].OwnedQuantity.Should().Be(2);
        owned[0].EquippedQuantity.Should().Be(0);
        owned[0].AvailableQuantity.Should().Be(2);
    }

    [Fact]
    public async Task GetOwnedGear_SomeEquipped_AvailableIsOwnedMinusEquipped()
    {
        var (service, gearRepo, equipRepo, gearDefs) = BuildServiceForOwnedGear();
        var playerId = Guid.NewGuid();

        // Owns 2 rings; one is equipped in Ring1 → available 1.
        gearRepo.Setup(g => g.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGear> { PlayerGear.Create(playerId, "gear_iron_ring", 2) });
        equipRepo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment>
            {
                PlayerEquipment.Create(playerId, EquipmentSlot.Ring1, "gear_iron_ring"),
            });
        gearDefs.Setup(g => g.GetById("gear_iron_ring")).Returns(new GearDefinition
            { Id = "gear_iron_ring", Name = "Iron Ring", Slot = "Ring1", BonusAttack = 1 });

        var owned = await service.GetOwnedGearAsync(playerId);

        owned.Should().HaveCount(1);
        owned[0].OwnedQuantity.Should().Be(2);
        owned[0].EquippedQuantity.Should().Be(1);
        owned[0].AvailableQuantity.Should().Be(1);
    }

    [Fact]
    public async Task GetOwnedGear_EmptyBag_ReturnsEmpty()
    {
        var (service, gearRepo, _, _) = BuildServiceForOwnedGear();
        var playerId = Guid.NewGuid();

        gearRepo.Setup(g => g.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGear>());

        var owned = await service.GetOwnedGearAsync(playerId);

        owned.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOwnedGear_UnknownDefinition_SkipsRow()
    {
        var (service, gearRepo, _, gearDefs) = BuildServiceForOwnedGear();
        var playerId = Guid.NewGuid();

        gearRepo.Setup(g => g.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGear> { PlayerGear.Create(playerId, "gear_removed", 1) });
        gearDefs.Setup(g => g.GetById("gear_removed")).Returns((GearDefinition?)null);

        var owned = await service.GetOwnedGearAsync(playerId);

        owned.Should().BeEmpty("a gear def no longer present in content is skipped");
    }

    // G2: GrantGearAsync — new row creates, existing row stacks
    private static (EquipmentService service, Mock<IPlayerGearRepository> gearRepo)
        BuildServiceForGrant()
    {
        var equipRepo = new Mock<IPlayerEquipmentRepository>();
        var gearDefs  = new Mock<IGearDefinitionProvider>();
        var auditLog  = new Mock<IAuditLogRepository>();
        var inventory = new Mock<IPlayerInventoryRepository>();
        var itemDefs  = new Mock<IItemDefinitionProvider>();
        var gearRepo  = new Mock<IPlayerGearRepository>();

        var service = new EquipmentService(
            equipRepo.Object, gearDefs.Object, auditLog.Object, inventory.Object, itemDefs.Object, gearRepo.Object, new Mock<IAchievementService>().Object,
            NoCommanderGear());
        return (service, gearRepo);
    }

    // The starter kit (owner, 2026-09-12): every definition marked Starter is granted once and put
    // on at registration, one per slot, so a new player is dressed before their first screen.
    [Fact]
    public async Task GrantStarterKitAsync_GrantsAndWears_EveryStarterPiece()
    {
        var (service, repo, gearDefs, auditLog, _, _, gearRepo) = BuildService();
        var playerId = Guid.NewGuid();
        var helm = HelmDef(); helm.Starter = true;
        var ring = new GearDefinition { Id = "gear_iron_ring", Name = "Iron Ring", Rarity = ItemRarity.Grey, Slot = "Ring1", BonusAttack = 1, Starter = true };
        var notStarter = new GearDefinition { Id = "gear_pano_helm", Name = "Pano", Rarity = ItemRarity.Orange, Slot = "Head" };
        gearDefs.Setup(d => d.GetAll()).Returns(new List<GearDefinition> { helm, notStarter, ring });
        gearDefs.Setup(d => d.GetById("gear_conscript_helm")).Returns(helm);
        gearDefs.Setup(d => d.GetById("gear_iron_ring")).Returns(ring);

        // Nothing owned before; the grant creates the row and the equip then finds it.
        var owned = new Dictionary<string, PlayerGear>();
        gearRepo.Setup(g => g.GetAsync(playerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string id, CancellationToken _) => owned.TryGetValue(id, out var pg) ? pg : null);
        gearRepo.Setup(g => g.CreateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerGear, CancellationToken>((pg, _) => owned[pg.GearDefinitionId] = pg)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.FindBySlotAsync(playerId, It.IsAny<EquipmentSlot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEquipment?)null);

        await service.GrantStarterKitAsync(playerId);

        owned.Keys.Should().BeEquivalentTo(new[] { "gear_conscript_helm", "gear_iron_ring" }, "only starter pieces are granted");
        repo.Verify(r => r.CreateAsync(It.Is<PlayerEquipment>(e => e.Slot == EquipmentSlot.Head && e.GearDefinitionId == "gear_conscript_helm"), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.CreateAsync(It.Is<PlayerEquipment>(e => e.Slot == EquipmentSlot.Ring1 && e.GearDefinitionId == "gear_iron_ring"), It.IsAny<CancellationToken>()), Times.Once);
        auditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(x => x.Action == "StarterKitGranted"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantStarterKitAsync_OverAnExistingAccount_GrantsOnlyWhatIsMissing_AndKeepsWornSlots()
    {
        // The admin CLI runs it over accounts that predate the kit: a helm already owned is not
        // granted twice, and a head already wearing something better keeps it.
        var (service, repo, gearDefs, auditLog, _, _, gearRepo) = BuildService();
        var playerId = Guid.NewGuid();
        var helm = HelmDef(); helm.Starter = true;
        var ring = new GearDefinition { Id = "gear_iron_ring", Name = "Iron Ring", Rarity = ItemRarity.Grey, Slot = "Ring1", Starter = true };
        gearDefs.Setup(d => d.GetAll()).Returns(new List<GearDefinition> { helm, ring });
        gearDefs.Setup(d => d.GetById("gear_conscript_helm")).Returns(helm);
        gearDefs.Setup(d => d.GetById("gear_iron_ring")).Returns(ring);

        var owned = new Dictionary<string, PlayerGear> { ["gear_conscript_helm"] = PlayerGear.Create(playerId, "gear_conscript_helm", 1) };
        gearRepo.Setup(g => g.GetAsync(playerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string id, CancellationToken _) => owned.TryGetValue(id, out var pg) ? pg : null);
        gearRepo.Setup(g => g.CreateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerGear, CancellationToken>((pg, _) => owned[pg.GearDefinitionId] = pg)
            .Returns(Task.CompletedTask);
        // The head already wears a Pano helm; the ring finger is bare.
        repo.Setup(r => r.FindBySlotAsync(playerId, EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerEquipment.Create(playerId, EquipmentSlot.Head, "gear_pano_helm"));
        repo.Setup(r => r.FindBySlotAsync(playerId, EquipmentSlot.Ring1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEquipment?)null);

        await service.GrantStarterKitAsync(playerId);

        gearRepo.Verify(g => g.CreateAsync(It.Is<PlayerGear>(pg => pg.GearDefinitionId == "gear_iron_ring"), It.IsAny<CancellationToken>()), Times.Once);
        gearRepo.Verify(g => g.CreateAsync(It.Is<PlayerGear>(pg => pg.GearDefinitionId == "gear_conscript_helm"), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.CreateAsync(It.Is<PlayerEquipment>(e => e.Slot == EquipmentSlot.Ring1), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.CreateAsync(It.Is<PlayerEquipment>(e => e.Slot == EquipmentSlot.Head), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.UpdateAsync(It.Is<PlayerEquipment>(e => e.Slot == EquipmentSlot.Head), It.IsAny<CancellationToken>()), Times.Never,
            "the Pano helm stays on");
    }

    [Fact]
    public async Task GrantStarterKitAsync_NothingMarked_DoesNothing()
    {
        var (service, repo, gearDefs, auditLog, _, _, gearRepo) = BuildService();
        gearDefs.Setup(d => d.GetAll()).Returns(new List<GearDefinition> { HelmDef() });

        await service.GrantStarterKitAsync(Guid.NewGuid());

        gearRepo.Verify(g => g.CreateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.CreateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()), Times.Never);
        auditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(x => x.Action == "StarterKitGranted"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantGearAsync_NewGear_CreatesRow()
    {
        var (service, gearRepo) = BuildServiceForGrant();
        var playerId = Guid.NewGuid();

        gearRepo.Setup(g => g.GetAsync(playerId, "gear_conscript_helm", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerGear?)null);
        gearRepo.Setup(g => g.CreateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await service.GrantGearAsync(playerId, "gear_conscript_helm", 1);

        gearRepo.Verify(g => g.CreateAsync(
            It.Is<PlayerGear>(pg => pg.GearDefinitionId == "gear_conscript_helm" && pg.Quantity == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        gearRepo.Verify(g => g.UpdateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantGearAsync_ExistingGear_StacksQuantity()
    {
        var (service, gearRepo) = BuildServiceForGrant();
        var playerId = Guid.NewGuid();

        var existing = PlayerGear.Create(playerId, "gear_iron_ring", 2);
        gearRepo.Setup(g => g.GetAsync(playerId, "gear_iron_ring", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        gearRepo.Setup(g => g.UpdateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await service.GrantGearAsync(playerId, "gear_iron_ring", 3);

        existing.Quantity.Should().Be(5, "2 existing + 3 granted = 5");
        gearRepo.Verify(g => g.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        gearRepo.Verify(g => g.CreateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // TICKET 46 — granting gear recounts the ABSOLUTE owned total (SUM of quantities) and writes the
    // EquipmentPiecesOwned achievement counter, so the counter can never drift on a re-grant.
    [Fact]
    public async Task GrantGearAsync_RecountsEquipmentPiecesOwned_Absolute()
    {
        var equipRepo = new Mock<IPlayerEquipmentRepository>();
        var gearDefs  = new Mock<IGearDefinitionProvider>();
        var auditLog  = new Mock<IAuditLogRepository>();
        var inventory = new Mock<IPlayerInventoryRepository>();
        var itemDefs  = new Mock<IItemDefinitionProvider>();
        var gearRepo  = new Mock<IPlayerGearRepository>();
        var achievements = new Mock<IAchievementService>();
        var playerId = Guid.NewGuid();

        gearRepo.Setup(g => g.GetAsync(playerId, "gear_iron_ring", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerGear?)null);
        gearRepo.Setup(g => g.CreateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // After the grant, the owned bag totals 25 pieces (e.g. 20 + 5) → absolute recount = 25.
        gearRepo.Setup(g => g.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGear>
            {
                PlayerGear.Create(playerId, "gear_a", 20),
                PlayerGear.Create(playerId, "gear_iron_ring", 5),
            });

        var service = new EquipmentService(
            equipRepo.Object, gearDefs.Object, auditLog.Object, inventory.Object, itemDefs.Object, gearRepo.Object, achievements.Object,
            NoCommanderGear());

        await service.GrantGearAsync(playerId, "gear_iron_ring", 5);

        achievements.Verify(a => a.SetCounterAsync(
            playerId, AchievementMetric.EquipmentPiecesOwned, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    // EquipAsync — G3 ownership gate
    [Fact]
    public async Task EquipAsync_OwnershipCheck_Fail_NotOwned()
    {
        var (service, repo, gearDefs, _, _, _, gearRepo) = BuildService();
        var playerId = Guid.NewGuid();
        const string gearId = "gear_conscript_helm";

        gearDefs.Setup(g => g.GetById(gearId)).Returns(HelmDef());
        gearRepo.Setup(r => r.GetAsync(playerId, gearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerGear?)null);

        var result = await service.EquipAsync(playerId, "Head", gearId);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("do not own");
        repo.Verify(r => r.CreateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EquipAsync_OwnershipCheck_Fail_AllEquipped()
    {
        var (service, repo, gearDefs, _, _, _, gearRepo) = BuildService();
        var playerId = Guid.NewGuid();
        const string gearId = "gear_conscript_helm";

        gearDefs.Setup(g => g.GetById(gearId)).Returns(HelmDef());
        gearRepo.Setup(r => r.GetAsync(playerId, gearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerGear.Create(playerId, gearId, 1));
        // Nothing in an equipment slot — the only copy is worn as COMMANDER.
        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment>().AsReadOnly());

        // The commander slot wears a copy too. Leaving it out of the count let ONE item be worn twice —
        // in an equipment slot and as commander — firing two independent procs in a single attack.
        var commander = new Mock<IPlayerCommanderGearRepository>();
        commander.Setup(r => r.FindAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommanderGear.Create(playerId, gearId));
        var service2 = BuildServiceWithCommander(repo, gearDefs, gearRepo, commander.Object);

        var result = await service2.EquipAsync(playerId, "Head", gearId);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("No available copy");
    }

    // Re-equipping the item ALREADY in that slot is a no-op, not a failure. The slot being overwritten
    // releases its copy, so a single-copy stack does not fail against itself.
    [Fact]
    public async Task EquipAsync_ReEquippingTheSameItemIntoTheSameSlot_Succeeds()
    {
        var (service, repo, gearDefs, _, _, _, gearRepo) = BuildService();
        var playerId = Guid.NewGuid();
        const string gearId = "gear_conscript_helm";

        gearDefs.Setup(g => g.GetById(gearId)).Returns(HelmDef());
        gearRepo.Setup(r => r.GetAsync(playerId, gearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerGear.Create(playerId, gearId, 1));
        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment>
            {
                PlayerEquipment.Create(playerId, EquipmentSlot.Head, gearId),
            }.AsReadOnly());

        var result = await service.EquipAsync(playerId, "Head", gearId);

        result.Success.Should().BeTrue("the slot being overwritten frees the copy it was holding");
    }

    [Fact]
    public async Task EquipAsync_OwnershipCheck_Success_SpareAvailable()
    {
        // Owns 2 helms; 1 already counted as equipped → spare = 1 → equip succeeds.
        var (service, repo, gearDefs, _, _, _, gearRepo) = BuildService();
        var playerId = Guid.NewGuid();
        const string gearId = "gear_conscript_helm";

        gearDefs.Setup(g => g.GetById(gearId)).Returns(HelmDef());
        gearRepo.Setup(r => r.GetAsync(playerId, gearId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerGear.Create(playerId, gearId, 2));
        // GetEquippedAsync returns 1 row for this def → equippedCount=1, available=2−1=1 → OK.
        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment>
            {
                PlayerEquipment.Create(playerId, EquipmentSlot.Head, gearId),
            }.AsReadOnly());
        repo.Setup(r => r.FindBySlotAsync(playerId, EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEquipment?)null);

        var result = await service.EquipAsync(playerId, "Head", gearId);

        result.Success.Should().BeTrue();
    }
}
