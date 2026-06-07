using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

// System 16 Slice 6 — token-shop provider load + startup-validation tests. Malformed catalogues are
// fed via a temp content dir (fresh per test); the real src/ROTA.Api/content JSON is never mutated.
// Def providers are mocked: every payloadId in the valid fixture resolves; the "bad payloadId" test
// makes one return null.
public class GauntletShopProviderTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _contentDir;

    public GauntletShopProviderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"rota_gauntlet_shop_test_{Guid.NewGuid():N}");
        _contentDir = Path.Combine(_tmpDir, "content");
        Directory.CreateDirectory(_contentDir);
        WriteShop(ValidShopJson);
    }

    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void WriteShop(string json) => File.WriteAllText(Path.Combine(_contentDir, "gauntlet_shop.json"), json);

    // Def providers that resolve everything by default (the valid fixture's payloadIds all exist).
    private static (Mock<IUnitDefinitionProvider>, Mock<ILegionDefinitionProvider>, Mock<IGearDefinitionProvider>) AllResolving()
    {
        var units = new Mock<IUnitDefinitionProvider>();
        units.Setup(p => p.GetById(It.IsAny<string>())).Returns((string id) => new UnitDefinition { Id = id });
        var legions = new Mock<ILegionDefinitionProvider>();
        legions.Setup(p => p.GetById(It.IsAny<string>())).Returns((string id) => new LegionDefinition { Id = id });
        var gear = new Mock<IGearDefinitionProvider>();
        gear.Setup(p => p.GetById(It.IsAny<string>())).Returns((string id) => new GearDefinition { Id = id });
        return (units, legions, gear);
    }

    private GauntletShopProvider Build(
        Mock<IUnitDefinitionProvider>? units = null,
        Mock<ILegionDefinitionProvider>? legions = null,
        Mock<IGearDefinitionProvider>? gear = null)
    {
        var (u, l, g) = AllResolving();
        return new GauntletShopProvider(_tmpDir, (units ?? u).Object, (legions ?? l).Object, (gear ?? g).Object);
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Provider_LoadsValidCatalogue()
    {
        var provider = Build();

        provider.GetAll().Should().HaveCount(5);
        provider.GetById("shop_unit")!.RewardKind.Should().Be(GauntletShopRewardKind.Unit);
        provider.GetById("shop_unit")!.IsOwnOnce.Should().BeTrue();
        provider.GetById("shop_gems")!.Amount.Should().Be(250);
        provider.GetById("shop_gems")!.MaxOwned.Should().BeNull("bundles are repeatable");
        provider.GetById("shop_gear")!.Currency.Should().Be(GauntletCurrency.Pitchfork);
        provider.GetById("nope").Should().BeNull();
    }

    [Fact]
    public void Provider_LoadsRealApiCatalogue()
    {
        // Point at the shipped Api content + REAL def providers — proves gauntlet_shop.json validates
        // against the actual units/legions/gear content.
        var apiContentRoot = FindApiContentRoot();
        var provider = new GauntletShopProvider(
            apiContentRoot,
            new UnitDefinitionProvider(apiContentRoot),
            new LegionDefinitionProvider(apiContentRoot),
            new GearDefinitionProvider(apiContentRoot));

        provider.GetAll().Should().NotBeEmpty();
        // At least one of each kind is present in the starter catalogue.
        provider.GetAll().Select(e => e.RewardKind).Distinct()
            .Should().Contain(new[]
            {
                GauntletShopRewardKind.Unit, GauntletShopRewardKind.Legion, GauntletShopRewardKind.Gear,
                GauntletShopRewardKind.GemBundle, GauntletShopRewardKind.StrikeRefill,
            });
    }

    // ── Startup validation ──────────────────────────────────────────────────────

    [Fact]
    public void DuplicateId_Throws()
    {
        WriteShop("""
        [
          { "id": "dup", "rewardKind": "GemBundle", "payloadId": "", "amount": 100, "currency": "Token", "price": 10 },
          { "id": "dup", "rewardKind": "StrikeRefill", "payloadId": "", "amount": 50, "currency": "Token", "price": 5 }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate id*dup*");
    }

    [Fact]
    public void ZeroPrice_Throws()
    {
        WriteShop("""
        [
          { "id": "freebie", "rewardKind": "GemBundle", "payloadId": "", "amount": 100, "currency": "Token", "price": 0 }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*price 0*must be > 0*");
    }

    [Fact]
    public void BadUnitPayloadId_Throws()
    {
        WriteShop("""
        [
          { "id": "shop_unit", "rewardKind": "Unit", "payloadId": "gen_does_not_exist", "currency": "Token", "price": 100, "maxOwned": 1 }
        ]
        """);

        // Unit provider resolves NOTHING for this test.
        var units = new Mock<IUnitDefinitionProvider>();
        units.Setup(p => p.GetById(It.IsAny<string>())).Returns((UnitDefinition?)null);

        var act = () => Build(units: units);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Unit payloadId 'gen_does_not_exist'*not defined*");
    }

    [Fact]
    public void GemBundleWithZeroAmount_Throws()
    {
        WriteShop("""
        [
          { "id": "shop_gems", "rewardKind": "GemBundle", "payloadId": "", "amount": 0, "currency": "Token", "price": 50 }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*amount 0*must be > 0*");
    }

    [Fact]
    public void OwnOnceKindWithoutMaxOwned1_Throws()
    {
        // A Unit entry missing maxOwned:1 would let the shop re-sell an owned item.
        WriteShop("""
        [
          { "id": "shop_unit", "rewardKind": "Unit", "payloadId": "gen_x", "currency": "Token", "price": 100 }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*must have maxOwned = 1*");
    }

    // The JSON enum converter throws on an unknown currency string at deserialize time.
    [Fact]
    public void UnknownCurrency_Throws()
    {
        WriteShop("""
        [
          { "id": "shop_gems", "rewardKind": "GemBundle", "payloadId": "", "amount": 100, "currency": "Doubloons", "price": 10 }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<Exception>("an unknown currency must not pass startup");
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────

    private const string ValidShopJson = """
    [
      { "id": "shop_unit",    "rewardKind": "Unit",         "payloadId": "gen_x",    "currency": "Token",     "price": 120, "maxOwned": 1 },
      { "id": "shop_legion",  "rewardKind": "Legion",       "payloadId": "legion_x", "currency": "Token",     "price": 200, "maxOwned": 1 },
      { "id": "shop_gear",    "rewardKind": "Gear",         "payloadId": "gear_x",   "currency": "Pitchfork", "price": 15,  "maxOwned": 1 },
      { "id": "shop_gems",    "rewardKind": "GemBundle",    "payloadId": "",         "amount": 250, "currency": "Token", "price": 50 },
      { "id": "shop_strikes", "rewardKind": "StrikeRefill", "payloadId": "",         "amount": 50,  "currency": "Token", "price": 25 }
    ]
    """;

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
