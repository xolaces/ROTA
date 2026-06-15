using FluentAssertions;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

public class MagicDefinitionProviderTests : IDisposable
{
    private readonly string _tmpDir;

    public MagicDefinitionProviderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"rota_magic_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tmpDir, "content"));
    }

    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    private string ContentPath => _tmpDir;

    private void WriteJson(string json)
        => File.WriteAllText(Path.Combine(_tmpDir, "content", "magics.json"), json);

    // Happy path

    [Fact]
    public void Provider_LoadsStarterAndPinnaclePlaceholderMagics()
    {
        // Point at the actual Api content directory
        var apiContentRoot = FindApiContentRoot();
        var provider = new MagicDefinitionProvider(apiContentRoot);

        var all = provider.GetAll();
        // 10 starter magics + 5 inert pinnacle placeholders (T33)
        // + 2 Gauntlet rank magics (System 16 Slice 1: Wrath + Blessing of the Ancients).
        all.Should().HaveCount(17);
        all.Should().Contain(m => m.Id == "magic_smite", "starter magics still load");

        var pinnacle = provider.GetById("magic_pinnacle_5000");
        pinnacle.Should().NotBeNull("a pinnacle placeholder magic was added");
        pinnacle!.ProcChance.Should().Be(0.0, "pinnacle placeholders are inert until designed by the first-claimant");
    }

    [Fact]
    public void Provider_GetById_ReturnsCorrectDefinition()
    {
        var apiContentRoot = FindApiContentRoot();
        var provider = new MagicDefinitionProvider(apiContentRoot);

        var smite = provider.GetById("magic_smite");

        smite.Should().NotBeNull();
        smite!.Name.Should().Be("Smite");
        smite.ProcChance.Should().BeApproximately(0.10, 0.001);
        smite.ProcAmount.Should().BeApproximately(0.60, 0.001);
    }

    [Fact]
    public void Provider_GetById_UnknownId_ReturnsNull()
    {
        var apiContentRoot = FindApiContentRoot();
        var provider = new MagicDefinitionProvider(apiContentRoot);

        provider.GetById("magic_does_not_exist").Should().BeNull();
    }

    // Startup validation

    [Fact]
    public void Provider_DuplicateId_ThrowsOnStartup()
    {
        WriteJson("""
        [
          { "id": "magic_x", "name": "X", "rarity": "White", "category": "Damage",
            "effectType": "DamageProc", "procChance": 0.5, "procAmount": 0.1 },
          { "id": "magic_x", "name": "X2", "rarity": "Green", "category": "Damage",
            "effectType": "DamageProc", "procChance": 0.5, "procAmount": 0.1 }
        ]
        """);

        var act = () => new MagicDefinitionProvider(ContentPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate id*magic_x*");
    }

    [Fact]
    public void Provider_ProcChanceAboveOne_ThrowsOnStartup()
    {
        WriteJson("""
        [
          { "id": "magic_bad", "name": "Bad", "rarity": "White", "category": "Damage",
            "effectType": "DamageProc", "procChance": 1.5, "procAmount": 0.1 }
        ]
        """);

        var act = () => new MagicDefinitionProvider(ContentPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*procChance*1.5*outside*");
    }

    [Fact]
    public void Provider_NegativeProcAmount_ThrowsOnStartup()
    {
        WriteJson("""
        [
          { "id": "magic_neg", "name": "Neg", "rarity": "White", "category": "Damage",
            "effectType": "DamageProc", "procChance": 0.5, "procAmount": -0.1 }
        ]
        """);

        var act = () => new MagicDefinitionProvider(ContentPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*negative procAmount*");
    }

    [Fact]
    public void Provider_MissingFile_ReturnsEmptyList()
    {
        var provider = new MagicDefinitionProvider(ContentPath);  // no magics.json written
        provider.GetAll().Should().BeEmpty();
    }

    // Helper

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
