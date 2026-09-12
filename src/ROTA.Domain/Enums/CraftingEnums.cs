namespace ROTA.Domain.Enums;

// crafting (D-018). Persisted only inside content JSON and audit strings today, but
// treated as append-only from the start: never renumber, only add.

/// <summary>What a recipe consumes. Mirrors Dawn, where recipes mixed owned units/legions with materials.</summary>
public enum CraftIngredientKind
{
    /// <summary>An inventory item — materials, tokens, event banners.</summary>
    Item   = 0,
    Unit   = 1,
    Legion = 2,
    Gear   = 3,
}

/// <summary>What a recipe produces. Materials-into-materials is deliberately out of scope (D-018).</summary>
public enum CraftOutputKind
{
    Unit   = 0,
    Legion = 1,
    Gear   = 2,
}

/// <summary>
/// Dawn's own recipe grouping — General / Events / Guild / Special — plus Reforge, ROTA's: a set
/// piece rebuilt around parts that only raids drop. Its own tab because there is one recipe per
/// piece, and ninety-odd of them in General would bury the sixteen recipes that were there.
/// </summary>
public enum CraftRecipeCategory
{
    General = 0,
    Events  = 1,
    Guild   = 2,
    Special = 3,
    Reforge = 4,
}
