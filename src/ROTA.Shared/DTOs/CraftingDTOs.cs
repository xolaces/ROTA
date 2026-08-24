namespace ROTA.Shared.DTOs;

// System 26 — crafting (D-018). Dawn-faithful: named ingredients in, a better-named definition out.

/// <summary>One ingredient line, hydrated with how many the caller actually holds.</summary>
public class CraftIngredientResponse
{
    public string Kind { get; set; } = string.Empty;   // Item | Unit | Legion | Gear
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Owned { get; set; }
    public bool Satisfied { get; set; }

    /// <summary>
    /// Set when the caller owns this ingredient but it is currently equipped, so crafting would
    /// dangle a legion slot or battalion reference. Names where, so the player can go free it.
    /// </summary>
    public string? BlockedBecauseEquipped { get; set; }
}

public class CraftRecipeResponse
{
    public string RecipeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;   // General | Events | Guild | Special

    public string OutputKind { get; set; } = string.Empty;
    public string OutputId { get; set; } = string.Empty;
    public string OutputName { get; set; } = string.Empty;
    public string OutputRarity { get; set; } = string.Empty;
    public int OutputQuantity { get; set; }

    public List<CraftIngredientResponse> Ingredients { get; set; } = new();
    public long GoldCost { get; set; }

    public bool CanCraft { get; set; }
    /// <summary>Short reason CanCraft is false, for the button's disabled state.</summary>
    public string? BlockedReason { get; set; }
    /// <summary>True when the caller already owns the output and it is an own-once kind.</summary>
    public bool AlreadyOwned { get; set; }
}

public class CraftCatalogueResponse
{
    public List<CraftRecipeResponse> Recipes { get; set; } = new();
    public long PlayerGold { get; set; }
}
