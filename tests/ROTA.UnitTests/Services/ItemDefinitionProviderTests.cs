using FluentAssertions;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

// System 22 Phase A Slice 7 — the Discernment quality-upgrade ladder validation on items.json.
public class ItemDefinitionProviderTests : IDisposable
{
    private readonly string _tmpDir;

    public ItemDefinitionProviderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"rota_item_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tmpDir, "content"));
    }

    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    private void WriteJson(string json)
        => File.WriteAllText(Path.Combine(_tmpDir, "content", "items.json"), json);

    [Fact]
    public void Provider_LoadsShippedLadder()
    {
        var provider = new ItemDefinitionProvider(FindApiContentRoot());
        // The seeded starter ladder.
        provider.GetById("mat_iron_shard")!.UpgradesTo.Should().Be("mat_arcane_dust");
        provider.GetById("statbag_minor")!.UpgradesTo.Should().Be("statbag_major");
        provider.GetById("sigil_ironcolossus_nightmare")!.UpgradesTo.Should().BeNull("Orange is the ceiling");
    }

    [Fact]
    public void Provider_UpgradesToUnknown_Throws()
    {
        WriteJson("""
        [
          { "id": "a", "name": "A", "rarity": "Grey", "type": "Material", "upgradesTo": "ghost" }
        ]
        """);
        var act = () => new ItemDefinitionProvider(_tmpDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*upgradesTo*ghost*does not exist*");
    }

    [Fact]
    public void Provider_UpgradesToNotHigherRarity_Throws()
    {
        WriteJson("""
        [
          { "id": "a", "name": "A", "rarity": "Blue", "type": "Material", "upgradesTo": "b" },
          { "id": "b", "name": "B", "rarity": "Green", "type": "Material" }
        ]
        """);
        var act = () => new ItemDefinitionProvider(_tmpDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*must be strictly higher rarity*");
    }

    [Fact]
    public void Provider_DuplicateId_Throws()
    {
        WriteJson("""
        [
          { "id": "a", "name": "A", "rarity": "Grey", "type": "Material" },
          { "id": "a", "name": "A2", "rarity": "Grey", "type": "Material" }
        ]
        """);
        var act = () => new ItemDefinitionProvider(_tmpDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate id*a*");
    }

    [Fact]
    public void Provider_ValidLadder_Loads()
    {
        WriteJson("""
        [
          { "id": "a", "name": "A", "rarity": "Grey",  "type": "Material", "upgradesTo": "b" },
          { "id": "b", "name": "B", "rarity": "White", "type": "Material" }
        ]
        """);
        var provider = new ItemDefinitionProvider(_tmpDir);
        provider.GetById("a")!.UpgradesTo.Should().Be("b");
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ROTA.Api");
            if (Directory.Exists(Path.Combine(candidate, "content")))
                return candidate;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
