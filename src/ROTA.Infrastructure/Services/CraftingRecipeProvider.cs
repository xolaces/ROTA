using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// System 26 (D-018). Loads <c>content/recipes.json</c> and validates it hard at BOOT — a recipe that
/// names an ingredient or output which does not exist, or that could mint value from nothing, must
/// fail startup rather than surface as a broken or exploitable craft in a player's hands. Mirrors the
/// eager-validating providers already used for masteries and the Gauntlet shop.
/// </summary>
public sealed class CraftingRecipeProvider : ICraftingRecipeProvider
{
    private readonly IReadOnlyDictionary<string, CraftingRecipe> _recipes;
    private readonly IItemDefinitionProvider _itemsProvider;
    private readonly IUnitDefinitionProvider _unitsProvider;
    private readonly ILegionDefinitionProvider _legionsProvider;
    private readonly IGearDefinitionProvider _gearProvider;

    public CraftingRecipeProvider(
        string contentRootPath,
        IItemDefinitionProvider items,
        IUnitDefinitionProvider units,
        ILegionDefinitionProvider legions,
        IGearDefinitionProvider gear)
    {
        _itemsProvider   = items;
        _unitsProvider   = units;
        _legionsProvider = legions;
        _gearProvider    = gear;

        var path = Path.Combine(contentRootPath, "content", "recipes.json");
        if (!File.Exists(path))
        {
            // No recipe file is a valid state (crafting simply offers nothing) — unlike a malformed one.
            _recipes = new Dictionary<string, CraftingRecipe>(StringComparer.Ordinal);
            return;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var list = JsonSerializer.Deserialize<List<CraftingRecipe>>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException("recipes.json deserialized to null.");

        var byId = new Dictionary<string, CraftingRecipe>(StringComparer.Ordinal);
        foreach (var r in list)
        {
            if (string.IsNullOrWhiteSpace(r.Id))
                throw new InvalidOperationException("recipes.json: a recipe is missing its id.");
            if (!byId.TryAdd(r.Id, r))
                throw new InvalidOperationException($"recipes.json: duplicate recipe id '{r.Id}'.");
        }

        foreach (var r in list)
        {
            if (r.Ingredients.Count == 0)
                throw new InvalidOperationException(
                    $"recipes.json: '{r.Id}' has no ingredients — it would mint its output from nothing.");

            if (r.OutputQuantity <= 0)
                throw new InvalidOperationException($"recipes.json: '{r.Id}' outputQuantity must be positive.");

            // Units and legions are own-once (a player either owns one or does not), so a recipe
            // claiming to yield several would silently grant one.
            if (r.OutputKind is CraftOutputKind.Unit or CraftOutputKind.Legion && r.OutputQuantity != 1)
                throw new InvalidOperationException(
                    $"recipes.json: '{r.Id}' yields {r.OutputKind}, which is own-once, so outputQuantity must be 1.");

            if (r.GoldCost < 0)
                throw new InvalidOperationException($"recipes.json: '{r.Id}' has a negative goldCost.");

            if (!OutputExists(r.OutputKind, r.OutputId))
                throw new InvalidOperationException(
                    $"recipes.json: '{r.Id}' outputs {r.OutputKind} '{r.OutputId}', which does not exist.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ing in r.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ing.Id))
                    throw new InvalidOperationException($"recipes.json: '{r.Id}' has an ingredient with no id.");
                if (ing.Quantity <= 0)
                    throw new InvalidOperationException(
                        $"recipes.json: '{r.Id}' ingredient '{ing.Id}' must have a positive quantity.");
                if (!IngredientExists(ing.Kind, ing.Id))
                    throw new InvalidOperationException(
                        $"recipes.json: '{r.Id}' needs {ing.Kind} '{ing.Id}', which does not exist.");
                if (!seen.Add($"{ing.Kind}:{ing.Id}"))
                    throw new InvalidOperationException(
                        $"recipes.json: '{r.Id}' lists {ing.Kind} '{ing.Id}' twice — merge the quantities.");

                // A recipe consuming its own output is either a no-op or, for own-once kinds, a way to
                // get the output back for free while pocketing the other ingredients' value.
                if (ing.Kind.ToString() == r.OutputKind.ToString() && ing.Id == r.OutputId)
                    throw new InvalidOperationException(
                        $"recipes.json: '{r.Id}' consumes its own output '{r.OutputId}'.");
            }
        }

        _recipes = byId;
    }

    private bool OutputExists(CraftOutputKind kind, string id) => kind switch
    {
        CraftOutputKind.Unit   => _unitsProvider.GetById(id) is not null,
        CraftOutputKind.Legion => _legionsProvider.GetById(id) is not null,
        CraftOutputKind.Gear   => _gearProvider.GetById(id) is not null,
        _ => false,
    };

    private bool IngredientExists(CraftIngredientKind kind, string id) => kind switch
    {
        CraftIngredientKind.Item   => _itemsProvider.GetById(id) is not null,
        CraftIngredientKind.Unit   => _unitsProvider.GetById(id) is not null,
        CraftIngredientKind.Legion => _legionsProvider.GetById(id) is not null,
        CraftIngredientKind.Gear   => _gearProvider.GetById(id) is not null,
        _ => false,
    };

    public CraftingRecipe? GetById(string id)
        => _recipes.TryGetValue(id, out var r) ? r : null;

    public IReadOnlyList<CraftingRecipe> GetAll() => _recipes.Values.ToList();
}
