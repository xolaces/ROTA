using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Validators;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Content;

/// <summary>System 29 — bad chest content must fail the boot, and the content we ship must load.</summary>
public class DailyChestPoolProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rota-chest-" + Guid.NewGuid().ToString("N"));

    public DailyChestPoolProviderTests() => Directory.CreateDirectory(Path.Combine(_root, "content"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir; best-effort */ }
    }

    private static IItemDefinitionProvider Items()
    {
        var items = new Mock<IItemDefinitionProvider>();
        items.Setup(i => i.GetById("potion")).Returns(new ItemDefinition { Id = "potion", Type = ItemType.Consumable });
        items.Setup(i => i.GetById("statbag")).Returns(new ItemDefinition { Id = "statbag", Type = ItemType.StatBag });
        items.Setup(i => i.GetById("helm")).Returns(new ItemDefinition { Id = "helm", Type = ItemType.Equipment });
        return items.Object;
    }

    private DailyChestPoolProvider Load(string json)
    {
        File.WriteAllText(Path.Combine(_root, "content", "daily_chest.json"), json);
        return new DailyChestPoolProvider(_root, Items());
    }

    [Fact]
    public void ValidPool_Loads_WithItsTotalWeight()
    {
        var p = Load("""{ "pool": [ { "itemDefinitionId": "potion", "quantity": 2, "weight": 5 } ] }""");

        p.Pool.Should().ContainSingle().Which.Should().Be(new DailyChestPoolEntry("potion", 2, 5));
        p.TotalWeight.Should().Be(5);
    }

    [Theory]
    [InlineData("""{ "pool": [] }""", "empty")]
    [InlineData("""{ "pool": [ { "itemDefinitionId": "nope", "quantity": 1, "weight": 1 } ] }""", "unknown item")]
    [InlineData("""{ "pool": [ { "itemDefinitionId": "statbag", "quantity": 1, "weight": 1 } ] }""", "StatBag")]
    [InlineData("""{ "pool": [ { "itemDefinitionId": "helm", "quantity": 1, "weight": 1 } ] }""", "Equipment")]
    [InlineData("""{ "pool": [ { "itemDefinitionId": "potion", "quantity": 0, "weight": 1 } ] }""", "quantity")]
    [InlineData("""{ "pool": [ { "itemDefinitionId": "potion", "quantity": 1, "weight": 0 } ] }""", "weight")]
    public void BadPool_FailsTheBoot(string json, string expectedInMessage)
    {
        var act = () => Load(json);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{expectedInMessage}*");
    }

    [Fact]
    public void MissingFile_FailsTheBoot()
    {
        var act = () => new DailyChestPoolProvider(_root, Items());

        act.Should().Throw<InvalidOperationException>().WithMessage("*missing*");
    }

    [Fact]
    public void ShippedContent_LoadsAgainstTheRealItemCatalogue()
    {
        var apiRoot = Path.Combine(FindRepoRoot(), "src", "ROTA.Api");
        var items   = new ItemDefinitionProvider(apiRoot);

        var p = new DailyChestPoolProvider(apiRoot, items);

        p.Pool.Should().NotBeEmpty();
        p.Pool.Should().OnlyContain(e => items.GetById(e.ItemDefinitionId)!.Type != ItemType.Sigil,
            "sigils stay out until the owner decides (System 29 §6.1, decision 0k)");
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void PickValidator_AcceptsOnlyZeroToFive(int index, bool valid)
        => new PickDailyChestRequestValidator().Validate(new PickDailyChestRequest { Index = index })
            .IsValid.Should().Be(valid);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
    }
}
