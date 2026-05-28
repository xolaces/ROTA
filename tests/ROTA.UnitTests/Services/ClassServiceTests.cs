using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class ClassServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private static IOptions<ClassConfig> BuildConfig()
    {
        var cfg = new ClassConfig
        {
            GuildStaminaRegenMinutes = 2.0,
            ClassUnlockLevels = new Dictionary<string, int>
            {
                ["Tier2"] = 5,
                ["Tier3"] = 100,
            },
            ConvergenceLevels = new Dictionary<int, string>
            {
                [2000]  = "Luminary",
                [5000]  = "Immortal",
                [7500]  = "Archon",
                [10000] = "Ancient",
                [15000] = "ElderAncient",
                [25000] = "Eternal",
            },
            RegenMinutesPerPoint = new Dictionary<string, Dictionary<string, double>>
            {
                ["Conscript"]        = new() { ["Energy"] = 5.0,   ["Stamina"] = 5.0  },
                ["Ironguard"]        = new() { ["Energy"] = 6.0,   ["Stamina"] = 3.0  },
                ["Arcanist"]         = new() { ["Energy"] = 3.0,   ["Stamina"] = 6.0  },
                ["Sentinel"]         = new() { ["Energy"] = 4.0,   ["Stamina"] = 4.0  },
                ["Stormguard"]       = new() { ["Energy"] = 7.0,   ["Stamina"] = 2.5  },
                ["Bloodguard"]       = new() { ["Energy"] = 5.0,   ["Stamina"] = 2.5  },
                ["Siegebreaker"]     = new() { ["Energy"] = 4.0,   ["Stamina"] = 3.0  },
                ["HighArcanist"]     = new() { ["Energy"] = 2.5,   ["Stamina"] = 7.0  },
                ["ShadowArcanist"]   = new() { ["Energy"] = 2.5,   ["Stamina"] = 5.0  },
                ["Runecaller"]       = new() { ["Energy"] = 3.0,   ["Stamina"] = 4.0  },
                ["IroncladSentinel"] = new() { ["Energy"] = 3.5,   ["Stamina"] = 3.5  },
                ["Voidwalker"]       = new() { ["Energy"] = 3.5,   ["Stamina"] = 3.5  },
                ["Dawnblade"]        = new() { ["Energy"] = 3.5,   ["Stamina"] = 3.5  },
                ["Luminary"]         = new() { ["Energy"] = 2.5,   ["Stamina"] = 2.5  },
                ["Immortal"]         = new() { ["Energy"] = 2.0,   ["Stamina"] = 2.0  },
                ["Archon"]           = new() { ["Energy"] = 1.75,  ["Stamina"] = 1.75 },
                ["Ancient"]          = new() { ["Energy"] = 1.5,   ["Stamina"] = 1.5  },
                ["ElderAncient"]     = new() { ["Energy"] = 1.25,  ["Stamina"] = 1.25 },
                ["Eternal"]          = new() { ["Energy"] = 1.0,   ["Stamina"] = 1.0  },
            },
        };
        return Options.Create(cfg);
    }

    private static ClassService BuildService(out Mock<IPlayerRepository> players)
    {
        players  = new Mock<IPlayerRepository>();
        var auditLog = new Mock<IAuditLogRepository>();
        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new ClassService(players.Object, auditLog.Object, BuildConfig());
    }

    private static Player MakePlayer(int level = 1, PlayerClass cls = PlayerClass.Conscript)
    {
        var p = Player.Create("test", "test@rota.test", "hash");
        // Advance level using dummy formula — level is set through XP carry-over
        for (int i = 1; i < level; i++)
            p.AddExperience(1000, _ => 1000);
        if (cls != PlayerClass.Conscript)
            p.SetClass(cls);
        return p;
    }

    // -----------------------------------------------------------------------
    // Default class
    // -----------------------------------------------------------------------

    [Fact]
    public void NewPlayer_DefaultClass_IsConscript()
    {
        var player = Player.Create("test", "test@rota.test", "hash");
        player.Class.Should().Be(PlayerClass.Conscript);
    }

    // -----------------------------------------------------------------------
    // GetAvailableChoices — path tier gates (L1-1000)
    // -----------------------------------------------------------------------

    [Fact]
    public void GetAvailableChoices_BelowLevel5_ReturnsEmpty()
    {
        var svc = BuildService(out _);
        svc.GetAvailableChoices(4, PlayerClass.Conscript).Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableChoices_AtLevel5_Conscript_ReturnsThreePaths()
    {
        var svc = BuildService(out _);
        var choices = svc.GetAvailableChoices(5, PlayerClass.Conscript);
        choices.Should().BeEquivalentTo(new[]
        {
            PlayerClass.Ironguard,
            PlayerClass.Arcanist,
            PlayerClass.Sentinel,
        });
    }

    [Fact]
    public void GetAvailableChoices_Level99_Conscript_ReturnsThreePaths()
    {
        var svc = BuildService(out _);
        svc.GetAvailableChoices(99, PlayerClass.Conscript).Should().HaveCount(3);
    }

    [Fact]
    public void GetAvailableChoices_IronguardPath_AtLevel100_ReturnsCorrectChoices()
    {
        var svc = BuildService(out _);
        var choices = svc.GetAvailableChoices(100, PlayerClass.Ironguard);
        choices.Should().BeEquivalentTo(new[]
        {
            PlayerClass.Stormguard,
            PlayerClass.Bloodguard,
            PlayerClass.Siegebreaker,
        });
    }

    [Fact]
    public void GetAvailableChoices_ArcanistPath_AtLevel100_ReturnsCorrectChoices()
    {
        var svc = BuildService(out _);
        var choices = svc.GetAvailableChoices(100, PlayerClass.Arcanist);
        choices.Should().BeEquivalentTo(new[]
        {
            PlayerClass.HighArcanist,
            PlayerClass.ShadowArcanist,
            PlayerClass.Runecaller,
        });
    }

    [Fact]
    public void GetAvailableChoices_SentinelPath_AtLevel100_ReturnsCorrectChoices()
    {
        var svc = BuildService(out _);
        var choices = svc.GetAvailableChoices(100, PlayerClass.Sentinel);
        choices.Should().BeEquivalentTo(new[]
        {
            PlayerClass.IroncladSentinel,
            PlayerClass.Voidwalker,
            PlayerClass.Dawnblade,
        });
    }

    [Fact]
    public void GetAvailableChoices_AtLevel500_ReturnsEmpty_AutoAdvanceOnly()
    {
        var svc = BuildService(out _);
        svc.GetAvailableChoices(500, PlayerClass.Ironguard).Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // AssignClassAsync — valid and invalid paths
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AssignClassAsync_ValidChoice_Succeeds()
    {
        var svc = BuildService(out var players);
        var player = MakePlayer(level: 5);
        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);

        var result = await svc.AssignClassAsync(player.Id, PlayerClass.Ironguard);

        result.Should().NotBeNull();
        result!.CurrentClass.Should().Be("Ironguard");
        player.Class.Should().Be(PlayerClass.Ironguard);
    }

    [Fact]
    public async Task AssignClassAsync_InvalidChoice_ReturnsNull()
    {
        var svc = BuildService(out var players);
        var player = MakePlayer(level: 3); // below Tier 2 unlock
        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);

        var result = await svc.AssignClassAsync(player.Id, PlayerClass.Ironguard);

        result.Should().BeNull();
        player.Class.Should().Be(PlayerClass.Conscript);
    }

    // -----------------------------------------------------------------------
    // ComputeAutoAdvance — path-based (L500, L1000)
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeAutoAdvance_Ironguard_AtLevel500_ReturnsLegendaryIronguard()
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(500, PlayerClass.Ironguard)
            .Should().Be(PlayerClass.LegendaryIronguard);
    }

    [Fact]
    public void ComputeAutoAdvance_LegendaryIronguard_AtLevel1000_ReturnsAscendantIronguard()
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(1000, PlayerClass.LegendaryIronguard)
            .Should().Be(PlayerClass.AscendantIronguard);
    }

    [Fact]
    public void ComputeAutoAdvance_BelowMilestone_ReturnsUnchanged()
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(499, PlayerClass.Ironguard)
            .Should().Be(PlayerClass.Ironguard);
    }

    // -----------------------------------------------------------------------
    // ComputeAutoAdvance — convergence (L2000+)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PlayerClass.AscendantIronguard)]
    [InlineData(PlayerClass.AscendantArcanist)]
    [InlineData(PlayerClass.AscendantDawnblade)]
    public void ComputeAutoAdvance_AnyAscendant_AtLevel2000_ReturnsLuminary(PlayerClass current)
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(2000, current).Should().Be(PlayerClass.Luminary);
    }

    [Fact]
    public void ComputeAutoAdvance_Luminary_AtLevel5000_ReturnsImmortal()
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(5000, PlayerClass.Luminary).Should().Be(PlayerClass.Immortal);
    }

    [Fact]
    public void ComputeAutoAdvance_Immortal_AtLevel7500_ReturnsArchon()
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(7500, PlayerClass.Immortal).Should().Be(PlayerClass.Archon);
    }

    [Fact]
    public void ComputeAutoAdvance_Archon_AtLevel10000_ReturnsAncient()
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(10000, PlayerClass.Archon).Should().Be(PlayerClass.Ancient);
    }

    [Fact]
    public void ComputeAutoAdvance_Ancient_AtLevel15000_ReturnsElderAncient()
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(15000, PlayerClass.Ancient).Should().Be(PlayerClass.ElderAncient);
    }

    [Fact]
    public void ComputeAutoAdvance_ElderAncient_AtLevel25000_ReturnsEternal()
    {
        var svc = BuildService(out _);
        svc.ComputeAutoAdvance(25000, PlayerClass.ElderAncient).Should().Be(PlayerClass.Eternal);
    }

    // -----------------------------------------------------------------------
    // IsConvergedClass
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PlayerClass.Luminary, true)]
    [InlineData(PlayerClass.Immortal, true)]
    [InlineData(PlayerClass.Archon, true)]
    [InlineData(PlayerClass.Ancient, true)]
    [InlineData(PlayerClass.ElderAncient, true)]
    [InlineData(PlayerClass.Eternal, true)]
    [InlineData(PlayerClass.AscendantIronguard, false)]
    [InlineData(PlayerClass.AscendantDawnblade, false)]
    [InlineData(PlayerClass.LegendaryIronguard, false)]
    [InlineData(PlayerClass.Ironguard, false)]
    [InlineData(PlayerClass.Conscript, false)]
    public void IsConvergedClass_CorrectForAllClasses(PlayerClass cls, bool expected)
    {
        var svc = BuildService(out _);
        svc.IsConvergedClass(cls).Should().Be(expected);
    }

    [Fact]
    public void GetAvailableChoices_Luminary_ReturnsEmpty_IsConverged()
    {
        var svc = BuildService(out _);
        svc.GetAvailableChoices(2001, PlayerClass.Luminary).Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Regen rates
    // -----------------------------------------------------------------------

    [Fact]
    public void GetRegenRates_Ironguard_Energy6_Stamina3()
    {
        var svc = BuildService(out _);
        var rates = svc.GetRegenRates(PlayerClass.Ironguard);
        rates.EnergyRegenMinutesPerPoint.Should().Be(6.0);
        rates.StaminaRegenMinutesPerPoint.Should().Be(3.0);
    }

    [Fact]
    public void GetRegenRates_Arcanist_Energy3_Stamina6()
    {
        var svc = BuildService(out _);
        var rates = svc.GetRegenRates(PlayerClass.Arcanist);
        rates.EnergyRegenMinutesPerPoint.Should().Be(3.0);
        rates.StaminaRegenMinutesPerPoint.Should().Be(6.0);
    }

    [Fact]
    public void GetRegenRates_LegendaryIronguard_SameAsIronguard()
    {
        var svc = BuildService(out _);
        var legendary = svc.GetRegenRates(PlayerClass.LegendaryIronguard);
        var base_     = svc.GetRegenRates(PlayerClass.Ironguard);
        legendary.EnergyRegenMinutesPerPoint.Should().Be(base_.EnergyRegenMinutesPerPoint);
        legendary.StaminaRegenMinutesPerPoint.Should().Be(base_.StaminaRegenMinutesPerPoint);
    }

    [Fact]
    public void GetRegenRates_AscendantArcanist_SameAsArcanist()
    {
        var svc = BuildService(out _);
        var ascendant = svc.GetRegenRates(PlayerClass.AscendantArcanist);
        var base_     = svc.GetRegenRates(PlayerClass.Arcanist);
        ascendant.EnergyRegenMinutesPerPoint.Should().Be(base_.EnergyRegenMinutesPerPoint);
        ascendant.StaminaRegenMinutesPerPoint.Should().Be(base_.StaminaRegenMinutesPerPoint);
    }

    [Fact]
    public void GetRegenRates_Luminary_Energy2_5_Stamina2_5()
    {
        var svc = BuildService(out _);
        var rates = svc.GetRegenRates(PlayerClass.Luminary);
        rates.EnergyRegenMinutesPerPoint.Should().Be(2.5);
        rates.StaminaRegenMinutesPerPoint.Should().Be(2.5);
    }

    [Fact]
    public void GetRegenRates_Ancient_Energy1_5_Stamina1_5()
    {
        var svc = BuildService(out _);
        var rates = svc.GetRegenRates(PlayerClass.Ancient);
        rates.EnergyRegenMinutesPerPoint.Should().Be(1.5);
        rates.StaminaRegenMinutesPerPoint.Should().Be(1.5);
    }

    [Fact]
    public void GetRegenRates_Eternal_Energy1_0_Stamina1_0()
    {
        var svc = BuildService(out _);
        var rates = svc.GetRegenRates(PlayerClass.Eternal);
        rates.EnergyRegenMinutesPerPoint.Should().Be(1.0);
        rates.StaminaRegenMinutesPerPoint.Should().Be(1.0);
    }

    [Theory]
    [InlineData(PlayerClass.Conscript)]
    [InlineData(PlayerClass.Ironguard)]
    [InlineData(PlayerClass.Luminary)]
    [InlineData(PlayerClass.Eternal)]
    public void GetRegenRates_GuildStamina_AlwaysTwo(PlayerClass cls)
    {
        var svc = BuildService(out _);
        svc.GetRegenRates(cls).GuildStaminaRegenMinutesPerPoint.Should().Be(2.0);
    }
}
