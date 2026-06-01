using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class LegionServiceTests
{
    // ----------------------------------------------------------------
    // FIXTURES (Slice 2 — ownership)
    // ----------------------------------------------------------------

    private record Bundle(
        LegionService               Service,
        Mock<IPlayerUnitRepository>   Units,
        Mock<IPlayerLegionRepository> Legions,
        Mock<IUnitDefinitionProvider> UnitDefs,
        Mock<ILegionDefinitionProvider> LegionDefs);

    private static Bundle Build()
    {
        var units      = new Mock<IPlayerUnitRepository>();
        var legions    = new Mock<IPlayerLegionRepository>();
        var unitDefs   = new Mock<IUnitDefinitionProvider>();
        var legionDefs = new Mock<ILegionDefinitionProvider>();

        var svc = new LegionService(units.Object, legions.Object,
                                    unitDefs.Object, legionDefs.Object);
        return new Bundle(svc, units, legions, unitDefs, legionDefs);
    }

    private static PlayerUnit MakeUnit(Guid playerId, string defId)
        => PlayerUnit.Create(playerId, defId);

    private static PlayerLegion MakeLegion(Guid playerId, string defId, bool isActive = false)
    {
        var l = PlayerLegion.Create(playerId, defId);
        if (isActive) l.SetActive(true);
        return l;
    }

    private static UnitDefinition MakeUnitDef(string id = "gen_ironward")
        => new()
        {
            Id          = id,
            Name        = "Ironward",
            UnitType    = UnitType.General,
            Rarity      = ItemRarity.Green,
            BaseAttack  = 80,
            BaseDefense = 60,
            Race        = UnitRace.Human,
            Role        = UnitRole.Tank,
            Attribute   = UnitAttribute.Strength,
            LegionBonus = 5,
        };

    private static LegionDefinition MakeLegionDef(string id = "legion_warband")
        => new()
        {
            Id           = id,
            Name         = "Free Warband",
            Rarity       = ItemRarity.White,
            PowerBonus   = 50,
            GeneralSlots = new() { new SlotSpec() },
            TroopSlots   = new() { new SlotSpec() },
        };

    // ----------------------------------------------------------------
    // GetOwnedUnitsAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetOwnedUnits_ReturnsHydratedUnits()
    {
        var b        = Build();
        var playerId = Guid.NewGuid();
        var def      = MakeUnitDef("gen_ironward");

        b.Units.Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerUnit> { MakeUnit(playerId, "gen_ironward") });
        b.UnitDefs.Setup(d => d.GetById("gen_ironward")).Returns(def);

        var result = await b.Service.GetOwnedUnitsAsync(playerId);

        result.Should().HaveCount(1);
        result[0].UnitDefinitionId.Should().Be("gen_ironward");
        result[0].Name.Should().Be("Ironward");
        result[0].UnitType.Should().Be("General");
        result[0].LegionBonus.Should().Be(5);
    }

    [Fact]
    public async Task GetOwnedUnits_ZeroOwned_ReturnsEmpty()
    {
        var b = Build();
        b.Units.Setup(r => r.GetOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerUnit>());

        var result = await b.Service.GetOwnedUnitsAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOwnedUnits_MissingDefinition_SkipsRow()
    {
        var b        = Build();
        var playerId = Guid.NewGuid();

        b.Units.Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerUnit> { MakeUnit(playerId, "orphan_id") });
        b.UnitDefs.Setup(d => d.GetById("orphan_id")).Returns((UnitDefinition?)null);

        var result = await b.Service.GetOwnedUnitsAsync(playerId);
        result.Should().BeEmpty("orphaned rows with no matching definition are silently skipped");
    }

    // ----------------------------------------------------------------
    // GetOwnedLegionsAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetOwnedLegions_ReturnsHydratedLegions()
    {
        var b        = Build();
        var playerId = Guid.NewGuid();
        var def      = MakeLegionDef("legion_warband");

        b.Legions.Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerLegion> { MakeLegion(playerId, "legion_warband", isActive: true) });
        b.LegionDefs.Setup(d => d.GetById("legion_warband")).Returns(def);

        var result = await b.Service.GetOwnedLegionsAsync(playerId);

        result.Should().HaveCount(1);
        result[0].LegionDefinitionId.Should().Be("legion_warband");
        result[0].Name.Should().Be("Free Warband");
        result[0].IsActive.Should().BeTrue();
        result[0].GeneralSlotCount.Should().Be(1);
        result[0].TroopSlotCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOwnedLegions_ZeroOwned_ReturnsEmpty()
    {
        var b = Build();
        b.Legions.Setup(r => r.GetOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerLegion>());

        var result = await b.Service.GetOwnedLegionsAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }
}
