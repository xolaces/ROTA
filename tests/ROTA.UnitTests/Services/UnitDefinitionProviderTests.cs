using FluentAssertions;
using System.Text.Json;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

public class UnitDefinitionProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public UnitDefinitionProviderTests()
        => Directory.CreateDirectory(Path.Combine(_tempDir, "content"));

    public void Dispose()
        => Directory.Delete(_tempDir, recursive: true);

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private void WriteUnits(object units)
        => File.WriteAllText(
            Path.Combine(_tempDir, "content", "units.json"),
            JsonSerializer.Serialize(units, _opts));

    [Fact]
    public void Provider_LoadsAllUnits_FromJson()
    {
        WriteUnits(new[]
        {
            new { id = "gen_test", name = "Test Gen", description = "", unitType = "General",
                  rarity = "Green", baseAttack = 100, baseDefense = 50,
                  race = "Human", role = "Melee", attribute = "Strength",
                  ability = (object?)null, isPassive = false, legionBonus = 5.0,
                  iconPath = "", acquisition = "" },
        });

        var provider = new UnitDefinitionProvider(_tempDir);

        provider.GetAll().Should().HaveCount(1);
        var def = provider.GetById("gen_test");
        def.Should().NotBeNull();
        def!.Name.Should().Be("Test Gen");
        def.UnitType.Should().Be(UnitType.General);
        def.LegionBonus.Should().Be(5.0);
    }

    [Fact]
    public void Provider_GetById_ReturnsNull_ForUnknownId()
    {
        WriteUnits(Array.Empty<object>());
        var provider = new UnitDefinitionProvider(_tempDir);

        provider.GetById("does_not_exist").Should().BeNull();
    }

    [Fact]
    public void Provider_Throws_OnDuplicateId()
    {
        WriteUnits(new[]
        {
            new { id = "dup", name = "A", description = "", unitType = "Troop",
                  rarity = "White", baseAttack = 10, baseDefense = 10,
                  race = "Human", role = "Melee", attribute = "Strength",
                  ability = (object?)null, isPassive = false, legionBonus = 0.0,
                  iconPath = "", acquisition = "" },
            new { id = "dup", name = "B", description = "", unitType = "Troop",
                  rarity = "White", baseAttack = 10, baseDefense = 10,
                  race = "Human", role = "Melee", attribute = "Strength",
                  ability = (object?)null, isPassive = false, legionBonus = 0.0,
                  iconPath = "", acquisition = "" },
        });

        var act = () => new UnitDefinitionProvider(_tempDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate id*");
    }

    [Fact]
    public void Provider_Throws_OnBadProcChance()
    {
        WriteUnits(new[]
        {
            new { id = "bad_chance", name = "Bad", description = "", unitType = "Troop",
                  rarity = "White", baseAttack = 10, baseDefense = 10,
                  race = "Human", role = "Melee", attribute = "Strength",
                  ability = new { procChance = 1.5, procAmount = 0.3, conditions = Array.Empty<object>() },
                  isPassive = false, legionBonus = 0.0, iconPath = "", acquisition = "" },
        });

        var act = () => new UnitDefinitionProvider(_tempDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*procChance*outside*");
    }

    [Fact]
    public void Provider_Throws_WhenTroopHasLegionBonus()
    {
        WriteUnits(new[]
        {
            new { id = "bad_troop", name = "Bad", description = "", unitType = "Troop",
                  rarity = "White", baseAttack = 10, baseDefense = 10,
                  race = "Human", role = "Melee", attribute = "Strength",
                  ability = (object?)null, isPassive = false, legionBonus = 5.0,
                  iconPath = "", acquisition = "" },
        });

        var act = () => new UnitDefinitionProvider(_tempDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*legionBonus*");
    }

    [Fact]
    public void Provider_MissingFile_ReturnsEmpty()
    {
        // No units.json written — provider should silently return empty, not throw.
        var provider = new UnitDefinitionProvider(_tempDir);
        provider.GetAll().Should().BeEmpty();
    }
}
