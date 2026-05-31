using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class MagicServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private record ServiceBundle(
        MagicService Service,
        Mock<IPlayerMagicRepository> MagicRepo,
        Mock<IMagicDefinitionProvider> Defs);

    private static ServiceBundle BuildService()
    {
        var repo = new Mock<IPlayerMagicRepository>();
        var defs = new Mock<IMagicDefinitionProvider>();
        return new ServiceBundle(new MagicService(repo.Object, defs.Object), repo, defs);
    }

    private static PlayerMagic MakeOwned(Guid playerId, string defId)
    {
        var m = PlayerMagic.Create(playerId, defId);
        return m;
    }

    private static MagicDefinition MakeDef(string id, string name = "Test Magic")
        => new()
        {
            Id         = id,
            Name       = name,
            Description = "desc",
            Rarity     = ItemRarity.Green,
            Category   = MagicCategory.Damage,
            EffectType = MagicEffectType.DamageProc,
            ProcChance = 0.10,
            ProcAmount = 0.5,
            Stacks     = true,
        };

    // -----------------------------------------------------------------------
    // GetOwnedMagicsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetOwnedMagicsAsync_ReturnsHydratedMagics()
    {
        var svc = BuildService();
        var playerId = Guid.NewGuid();
        var def = MakeDef("magic_smite", "Smite");
        var owned = MakeOwned(playerId, def.Id);

        svc.MagicRepo
            .Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMagic> { owned });
        svc.Defs
            .Setup(d => d.GetById("magic_smite"))
            .Returns(def);

        var result = await svc.Service.GetOwnedMagicsAsync(playerId);

        result.Should().HaveCount(1);
        result[0].MagicDefinitionId.Should().Be("magic_smite");
        result[0].Name.Should().Be("Smite");
        result[0].EffectType.Should().Be("DamageProc");
        result[0].ProcChance.Should().BeApproximately(0.10, 0.001);
    }

    [Fact]
    public async Task GetOwnedMagicsAsync_ZeroOwned_ReturnsEmpty()
    {
        var svc = BuildService();
        var playerId = Guid.NewGuid();

        svc.MagicRepo
            .Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMagic>());

        var result = await svc.Service.GetOwnedMagicsAsync(playerId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOwnedMagicsAsync_MissingDefinition_SkipsRow()
    {
        var svc = BuildService();
        var playerId = Guid.NewGuid();
        var owned = MakeOwned(playerId, "magic_unknown");

        svc.MagicRepo
            .Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMagic> { owned });
        svc.Defs
            .Setup(d => d.GetById("magic_unknown"))
            .Returns((MagicDefinition?)null);

        var result = await svc.Service.GetOwnedMagicsAsync(playerId);

        result.Should().BeEmpty("orphaned rows with no matching definition are silently skipped");
    }
}
