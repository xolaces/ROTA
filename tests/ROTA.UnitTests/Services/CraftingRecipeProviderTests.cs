using FluentAssertions;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

// System 26 (D-018) — recipe content is the whole balance surface for crafting, and a bad recipe is
// either a broken craft or free value. Every rule below must fail the BOOT, not a player's click.
public class CraftingRecipeProviderTests : IDisposable
{
    private readonly string _tmpDir;

    public CraftingRecipeProviderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"rota_recipe_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tmpDir, "content"));
    }

    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    private void WriteJson(string json)
        => File.WriteAllText(Path.Combine(_tmpDir, "content", "recipes.json"), json);

    // Minimal stub providers: "known" ids resolve, everything else does not.
    private sealed class StubItems : IItemDefinitionProvider
    {
        public ItemDefinition? GetById(string id)
            => id == "mat_known" ? new ItemDefinition { Id = id, Name = "Known Material" } : null;
        public IReadOnlyList<ItemDefinition> GetAll() => Array.Empty<ItemDefinition>();
    }
    private sealed class StubUnits : IUnitDefinitionProvider
    {
        public UnitDefinition? GetById(string id)
            => id is "unit_in" or "unit_out" ? new UnitDefinition { Id = id, Name = id } : null;
        public IReadOnlyList<UnitDefinition> GetAll() => Array.Empty<UnitDefinition>();
    }
    private sealed class StubLegions : ILegionDefinitionProvider
    {
        public LegionDefinition? GetById(string id)
            => id is "legion_in" or "legion_out" ? new LegionDefinition { Id = id, Name = id } : null;
        public IReadOnlyList<LegionDefinition> GetAll() => Array.Empty<LegionDefinition>();
    }
    private sealed class StubGear : IGearDefinitionProvider
    {
        public GearDefinition? GetById(string id)
            => id is "gear_in" or "gear_out" ? new GearDefinition { Id = id, Name = id } : null;
        public IReadOnlyList<GearDefinition> GetAll() => Array.Empty<GearDefinition>();
        public IReadOnlyList<GearDefinition> GetBySlot(string slot) => Array.Empty<GearDefinition>();
    }

    private CraftingRecipeProvider Build(string dir)
        => new(dir, new StubItems(), new StubUnits(), new StubLegions(), new StubGear());

    private Action Act(string json)
    {
        WriteJson(json);
        return () => Build(_tmpDir);
    }

    private const string ValidRecipe = """
    [
      { "id": "r1", "name": "R1", "category": "General", "outputKind": "Unit", "outputId": "unit_out",
        "outputQuantity": 1, "goldCost": 100,
        "ingredients": [ { "kind": "Unit", "id": "unit_in", "quantity": 1 },
                         { "kind": "Item", "id": "mat_known", "quantity": 3 } ] }
    ]
    """;

    [Fact]
    public void ValidRecipe_Loads()
    {
        WriteJson(ValidRecipe);
        var p = Build(_tmpDir);

        var r = p.GetById("r1")!;
        r.OutputKind.Should().Be(CraftOutputKind.Unit);
        r.Category.Should().Be(CraftRecipeCategory.General);
        r.Ingredients.Should().HaveCount(2);
        r.Ingredients[1].Quantity.Should().Be(3);
        p.GetAll().Should().HaveCount(1);
    }

    // A missing file means crafting offers nothing — a valid state, unlike a malformed file.
    [Fact]
    public void MissingFile_YieldsEmptyCatalogue_WithoutThrowing()
        => Build(_tmpDir).GetAll().Should().BeEmpty();

    [Fact]
    public void DuplicateRecipeId_Throws()
        => Act("""
        [
          { "id": "dup", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 1,
            "ingredients": [ { "kind": "Item", "id": "mat_known", "quantity": 1 } ] },
          { "id": "dup", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 1,
            "ingredients": [ { "kind": "Item", "id": "mat_known", "quantity": 1 } ] }
        ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*duplicate recipe id*");

    // The important one: no ingredients means the recipe mints its output from nothing.
    [Fact]
    public void NoIngredients_Throws()
        => Act("""
        [ { "id": "free", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 1, "ingredients": [] } ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*mint its output from nothing*");

    [Fact]
    public void UnknownIngredient_Throws()
        => Act("""
        [ { "id": "r", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 1,
            "ingredients": [ { "kind": "Item", "id": "ghost", "quantity": 1 } ] } ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*Item 'ghost', which does not exist*");

    [Fact]
    public void UnknownOutput_Throws()
        => Act("""
        [ { "id": "r", "outputKind": "Legion", "outputId": "ghost", "outputQuantity": 1,
            "ingredients": [ { "kind": "Item", "id": "mat_known", "quantity": 1 } ] } ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*outputs Legion 'ghost'*");

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveIngredientQuantity_Throws(int qty)
        => Act($$"""
        [ { "id": "r", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 1,
            "ingredients": [ { "kind": "Item", "id": "mat_known", "quantity": {{qty}} } ] } ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*positive quantity*");

    // Units and legions are own-once, so a recipe promising several would silently grant one.
    [Fact]
    public void OwnOnceOutput_WithQuantityAboveOne_Throws()
        => Act("""
        [ { "id": "r", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 2,
            "ingredients": [ { "kind": "Item", "id": "mat_known", "quantity": 1 } ] } ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*own-once*");

    // Gear stacks, so a multi-yield gear recipe is legitimate.
    [Fact]
    public void GearOutput_MayYieldMoreThanOne()
    {
        WriteJson("""
        [ { "id": "r", "outputKind": "Gear", "outputId": "gear_out", "outputQuantity": 3,
            "ingredients": [ { "kind": "Item", "id": "mat_known", "quantity": 1 } ] } ]
        """);
        Build(_tmpDir).GetById("r")!.OutputQuantity.Should().Be(3);
    }

    // Consuming your own output is either a no-op or, for own-once kinds, a way to get it back free
    // while pocketing the value of everything else in the recipe.
    [Fact]
    public void RecipeConsumingItsOwnOutput_Throws()
        => Act("""
        [ { "id": "r", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 1,
            "ingredients": [ { "kind": "Unit", "id": "unit_out", "quantity": 1 } ] } ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*consumes its own output*");

    // Same ingredient twice would let a sloppy edit halve the real cost after a merge.
    [Fact]
    public void DuplicateIngredientLine_Throws()
        => Act("""
        [ { "id": "r", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 1,
            "ingredients": [ { "kind": "Item", "id": "mat_known", "quantity": 1 },
                             { "kind": "Item", "id": "mat_known", "quantity": 2 } ] } ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*twice*");

    [Fact]
    public void NegativeGoldCost_Throws()
        => Act("""
        [ { "id": "r", "outputKind": "Unit", "outputId": "unit_out", "outputQuantity": 1, "goldCost": -5,
            "ingredients": [ { "kind": "Item", "id": "mat_known", "quantity": 1 } ] } ]
        """).Should().Throw<InvalidOperationException>().WithMessage("*negative goldCost*");

    // The shipped catalogue must satisfy every rule above against the REAL content providers.
    [Fact]
    public void ShippedRecipes_AreValid()
    {
        var root = FindApiContentRoot();
        var provider = new CraftingRecipeProvider(
            root,
            new ItemDefinitionProvider(root),
            new UnitDefinitionProvider(root),
            new LegionDefinitionProvider(root),
            new GearDefinitionProvider(root));

        var all = provider.GetAll();
        all.Should().NotBeEmpty("the shipped catalogue seeds the crafting area");
        all.Should().OnlyContain(r => r.Ingredients.Count > 0);
        provider.GetById("craft_ironward_ii")!.OutputId.Should().Be("gen_ironward_ii");
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
