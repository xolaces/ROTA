using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

/// <summary>
/// System 26 — loads and validates <c>content/recipes.json</c> at boot (D-018). Singleton, eagerly
/// constructed in Program.cs so a malformed recipe fails startup rather than a player's craft.
/// </summary>
public interface ICraftingRecipeProvider
{
    CraftingRecipe? GetById(string id);
    IReadOnlyList<CraftingRecipe> GetAll();
}
