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

        // Was `HaveCount(17)`. An exact count is not a property of the loader — it is a property of
        // whatever content happened to exist the day the test was written, so every content addition
        // broke a test that had found no bug. A floor still catches the failure that matters (the
        // file did not load, or loaded empty); the named rows below carry the real assertions.
        all.Should().HaveCountGreaterThanOrEqualTo(17, "the shipped catalogue never shrinks");
        all.Should().Contain(m => m.Id == "magic_smite", "starter magics still load");

        // All five pinnacle placeholders are present AND inert. They are gated on pinnacle levels the
        // owner has not sized yet, so a non-zero value here means someone decided that by accident.
        foreach (var level in new[] { 5000, 7500, 10000, 15000, 25000 })
        {
            var placeholder = provider.GetById($"magic_pinnacle_{level}");
            placeholder.Should().NotBeNull($"the pinnacle-{level} placeholder should load");
            placeholder!.ProcChance.Should().Be(0.0,
                "pinnacle placeholders stay inert until the first-claimant designs them");
            placeholder.ProcAmount.Should().Be(0.0,
                "an inert placeholder has no amount either — a live amount at zero chance is a trap");
        }
    }

    [Fact]
    public void Provider_GetById_ReturnsCorrectDefinition()
    {
        var apiContentRoot = FindApiContentRoot();
        var provider = new MagicDefinitionProvider(apiContentRoot);

        var smite = provider.GetById("magic_smite");

        smite.Should().NotBeNull();
        smite!.Name.Should().Be("Smite");
        // Owner decision 2026-09-07 — Smite is the pinnacle damage magic, so it sits at
        // the top of the Orange band. MagicProcBandTests owns the balance invariants;
        // this test only proves GetById resolves a real definition.
        smite.Rarity.Should().Be(ROTA.Domain.Enums.ItemRarity.Orange);
        smite.ProcChance.Should().BeApproximately(0.18, 0.001);
        smite.ProcAmount.Should().BeApproximately(1.10, 0.001);
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
