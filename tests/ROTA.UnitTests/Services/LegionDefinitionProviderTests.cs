using FluentAssertions;
using System.Text.Json;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

public class LegionDefinitionProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LegionDefinitionProviderTests()
        => Directory.CreateDirectory(Path.Combine(_tempDir, "content"));

    public void Dispose()
        => Directory.Delete(_tempDir, recursive: true);

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private void WriteLegions(object legions)
        => File.WriteAllText(
            Path.Combine(_tempDir, "content", "legions.json"),
            JsonSerializer.Serialize(legions, _opts));

    [Fact]
    public void Provider_LoadsAllLegions_FromJson()
    {
        WriteLegions(new[]
        {
            new { id = "legion_test", name = "Test Legion", description = "",
                  rarity = "White", powerBonus = 50.0,
                  generalSlots = new[] { new { constraintType = "None", constraintValue = (string?)null } },
                  troopSlots   = new[] { new { constraintType = "None", constraintValue = (string?)null } },
                  iconPath = "", acquisition = "" },
        });

        var provider = new LegionDefinitionProvider(_tempDir);

        provider.GetAll().Should().HaveCount(1);
        var def = provider.GetById("legion_test");
        def.Should().NotBeNull();
        def!.PowerBonus.Should().Be(50.0);
        def.GeneralSlots.Should().HaveCount(1);
        def.TroopSlots.Should().HaveCount(1);
    }

    [Fact]
    public void Provider_GetById_ReturnsNull_ForUnknownId()
    {
        WriteLegions(Array.Empty<object>());
        var provider = new LegionDefinitionProvider(_tempDir);

        provider.GetById("does_not_exist").Should().BeNull();
    }

    [Fact]
    public void Provider_Throws_OnDuplicateId()
    {
        WriteLegions(new[]
        {
            new { id = "dup", name = "A", description = "", rarity = "White", powerBonus = 0.0,
                  generalSlots = Array.Empty<object>(), troopSlots = Array.Empty<object>(),
                  iconPath = "", acquisition = "" },
            new { id = "dup", name = "B", description = "", rarity = "White", powerBonus = 0.0,
                  generalSlots = Array.Empty<object>(), troopSlots = Array.Empty<object>(),
                  iconPath = "", acquisition = "" },
        });

        var act = () => new LegionDefinitionProvider(_tempDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate id*");
    }

    [Fact]
    public void Provider_Throws_OnBadConstraintValue_RoleSlot()
    {
        WriteLegions(new[]
        {
            new { id = "bad_constraint", name = "Bad", description = "", rarity = "White", powerBonus = 0.0,
                  generalSlots = new[] { new { constraintType = "Role", constraintValue = "InvalidRole" } },
                  troopSlots   = Array.Empty<object>(),
                  iconPath = "", acquisition = "" },
        });

        var act = () => new LegionDefinitionProvider(_tempDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not a valid*");
    }

    [Fact]
    public void Provider_Throws_OnMissingConstraintValue_WhenTypeIsNotNone()
    {
        WriteLegions(new[]
        {
            new { id = "missing_val", name = "X", description = "", rarity = "White", powerBonus = 0.0,
                  generalSlots = new[] { new { constraintType = "Race", constraintValue = (string?)null } },
                  troopSlots   = Array.Empty<object>(),
                  iconPath = "", acquisition = "" },
        });

        var act = () => new LegionDefinitionProvider(_tempDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*requires constraintValue*");
    }

    [Fact]
    public void Provider_MissingFile_ReturnsEmpty()
    {
        var provider = new LegionDefinitionProvider(_tempDir);
        provider.GetAll().Should().BeEmpty();
    }
}
