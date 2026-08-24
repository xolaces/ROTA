using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

/// <summary>
/// System 26 — a crafting recipe (D-018). Dawn-faithful: named ingredients in, a better-named
/// DEFINITION out (<c>Bucket Brigade II ← Riot II + Bucket Brigade</c>). There is no levelling —
/// all power lives in the output's own definition, so tuning a tier is a content edit.
/// </summary>
public class CraftingRecipe
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Dawn's own grouping — drives shop tabs and, for Events/Guild, availability.</summary>
    public CraftRecipeCategory Category { get; set; }

    public CraftOutputKind OutputKind { get; set; }
    public string OutputId { get; set; } = string.Empty;

    /// <summary>
    /// Units and legions are own-once, so their recipes must produce exactly 1 — validated at boot.
    /// Gear stacks, so a gear recipe may yield more.
    /// </summary>
    public int OutputQuantity { get; set; } = 1;

    public List<CraftIngredient> Ingredients { get; set; } = new();

    /// <summary>Optional gold component. Another sink for the soft currency; 0 = free.</summary>
    public long GoldCost { get; set; }

    /// <summary>
    /// null = a core recipe, always visible (D-018). Non-null ties the recipe to an event/guild
    /// window so it is offered only while that content runs — how the Gauntlet and guilds keep
    /// exclusive rewards.
    /// </summary>
    public string? EventKey { get; set; }

    public string IconPath { get; set; } = string.Empty;
}

/// <summary>One named ingredient and how many of it a recipe consumes.</summary>
public class CraftIngredient
{
    public CraftIngredientKind Kind { get; set; }
    public string Id { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}
