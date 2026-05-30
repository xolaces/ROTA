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
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private static (EquipmentService service,
                    Mock<IPlayerEquipmentRepository> repo,
                    Mock<IGearDefinitionProvider> gearDefs,
                    Mock<IAuditLogRepository> auditLog)
        BuildService()
    {
        var repo     = new Mock<IPlayerEquipmentRepository>();
        var gearDefs = new Mock<IGearDefinitionProvider>();
        var auditLog = new Mock<IAuditLogRepository>();

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.CreateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new EquipmentService(repo.Object, gearDefs.Object, auditLog.Object);
        return (service, repo, gearDefs, auditLog);
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

    // -----------------------------------------------------------------------
    // EquipAsync — success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Equip_ValidItem_ReturnsSuccess()
    {
        var (service, repo, gearDefs, _) = BuildService();

        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(HelmDef());
        repo.Setup(r => r.FindBySlotAsync(It.IsAny<Guid>(), EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEquipment?)null);

        var result = await service.EquipAsync(Guid.NewGuid(), "Head", "gear_conscript_helm");

        result.Success.Should().BeTrue();
        result.Item.Should().NotBeNull();
        result.Item!.Slot.Should().Be("Head");
        result.Item.BonusDefense.Should().Be(1);
        repo.Verify(r => r.CreateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // EquipAsync — unknown slot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Equip_UnknownSlot_ReturnsFailure()
    {
        var (service, _, gearDefs, _) = BuildService();

        var result = await service.EquipAsync(Guid.NewGuid(), "Shoulder", "gear_conscript_helm");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Unknown slot");
    }

    // -----------------------------------------------------------------------
    // EquipAsync — gear not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Equip_GearNotFound_ReturnsFailure()
    {
        var (service, _, gearDefs, _) = BuildService();

        gearDefs.Setup(g => g.GetById("nonexistent")).Returns((GearDefinition?)null);

        var result = await service.EquipAsync(Guid.NewGuid(), "Head", "nonexistent");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
    }

    // -----------------------------------------------------------------------
    // EquipAsync — slot mismatch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Equip_SlotMismatch_ReturnsFailure()
    {
        var (service, _, gearDefs, _) = BuildService();

        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(HelmDef()); // Slot=Head

        var result = await service.EquipAsync(Guid.NewGuid(), "Neck", "gear_conscript_helm");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("belongs to slot");
    }

    // -----------------------------------------------------------------------
    // EquipAsync — existing slot swaps gear (UpdateAsync, not CreateAsync)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Equip_ExistingSlot_SwapsGear()
    {
        var (service, repo, gearDefs, _) = BuildService();

        var existingRow = PlayerEquipment.Create(Guid.NewGuid(), EquipmentSlot.Head, "gear_old_helm");
        gearDefs.Setup(g => g.GetById("gear_conscript_helm")).Returns(HelmDef());
        repo.Setup(r => r.FindBySlotAsync(It.IsAny<Guid>(), EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRow);

        var result = await service.EquipAsync(Guid.NewGuid(), "Head", "gear_conscript_helm");

        result.Success.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(existingRow, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.CreateAsync(It.IsAny<PlayerEquipment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // UnequipAsync — success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Unequip_EquippedSlot_ReturnsSuccess()
    {
        var (service, repo, _, _) = BuildService();

        var existingRow = PlayerEquipment.Create(Guid.NewGuid(), EquipmentSlot.Head, "gear_conscript_helm");
        repo.Setup(r => r.FindBySlotAsync(It.IsAny<Guid>(), EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRow);

        var result = await service.UnequipAsync(Guid.NewGuid(), "Head");

        result.Success.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(existingRow, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // UnequipAsync — empty slot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Unequip_EmptySlot_ReturnsFailure()
    {
        var (service, repo, _, _) = BuildService();

        repo.Setup(r => r.FindBySlotAsync(It.IsAny<Guid>(), EquipmentSlot.Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEquipment?)null);

        var result = await service.UnequipAsync(Guid.NewGuid(), "Head");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("No item equipped");
    }

    // -----------------------------------------------------------------------
    // GetEffectiveCombatDataAsync — gear adds stat bonuses
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetEffectiveCombatData_WithGear_AddsStatBonuses()
    {
        // Two equipped items: helm (DEF+1) + iron ring (ATK+1); base ATK=10, DEF=10
        // Expected: effective ATK=11, DEF=13 (helm + collar + chest each add 1 DEF, but spec says +3 DEF total)
        // Per spec test 8: "BonusAttack=1, BonusDefense=3 total; base ATK=10, DEF=10; effective = ATK=11, DEF=13"
        var (service, repo, gearDefs, _) = BuildService();
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

    // -----------------------------------------------------------------------
    // GetEffectiveCombatDataAsync — mount returns proc data
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetEffectiveCombatData_WithMount_ReturnsProcData()
    {
        var (service, repo, gearDefs, _) = BuildService();
        var playerId = Guid.NewGuid();

        var mountRow = PlayerEquipment.Create(playerId, EquipmentSlot.Mount, "gear_draft_horse");

        repo.Setup(r => r.GetEquippedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEquipment> { mountRow });

        gearDefs.Setup(g => g.GetById("gear_draft_horse")).Returns(MountDef());

        var result = await service.GetEffectiveCombatDataAsync(playerId, 10, 10);

        result.MountProc.Should().NotBeNull("a mount with procChance is equipped");
        result.MountProc!.ProcChance.Should().BeApproximately(0.05, 1e-9);
    }
}
