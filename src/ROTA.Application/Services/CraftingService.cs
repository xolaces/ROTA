using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// System 26 — crafting (D-018). Dawn-faithful: a recipe names exact ingredients and produces a
/// better-named DEFINITION (<c>Bucket Brigade II ← Riot II + Bucket Brigade</c>). No levelling — all
/// power lives in the output's own definition.
///
/// Slice 1 is READ-ONLY: the catalogue plus everything the client needs to render a craft button's
/// enabled/disabled state. The consuming transaction is slice 2.
/// </summary>
public sealed class CraftingService : ICraftingService
{
    private readonly ICraftingRecipeProvider _recipes;
    private readonly IItemDefinitionProvider _itemDefs;
    private readonly IUnitDefinitionProvider _unitDefs;
    private readonly ILegionDefinitionProvider _legionDefs;
    private readonly IGearDefinitionProvider _gearDefs;
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IPlayerUnitRepository _units;
    private readonly IPlayerLegionRepository _legions;
    private readonly IPlayerGearRepository _gear;
    private readonly IPlayerLegionSlotRepository _slots;
    private readonly IPlayerRepository _players;

    public CraftingService(
        ICraftingRecipeProvider recipes,
        IItemDefinitionProvider itemDefs,
        IUnitDefinitionProvider unitDefs,
        ILegionDefinitionProvider legionDefs,
        IGearDefinitionProvider gearDefs,
        IPlayerInventoryRepository inventory,
        IPlayerUnitRepository units,
        IPlayerLegionRepository legions,
        IPlayerGearRepository gear,
        IPlayerLegionSlotRepository slots,
        IPlayerRepository players)
    {
        _recipes    = recipes;
        _itemDefs   = itemDefs;
        _unitDefs   = unitDefs;
        _legionDefs = legionDefs;
        _gearDefs   = gearDefs;
        _inventory  = inventory;
        _units      = units;
        _legions    = legions;
        _gear       = gear;
        _slots      = slots;
        _players    = players;
    }

    public async Task<CraftCatalogueResponse> GetCatalogueAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        var gold = player?.Gold ?? 0;

        // One read per holding type for the whole catalogue rather than per ingredient line.
        var holdings = await LoadHoldingsAsync(playerId, ct);

        var rows = new List<CraftRecipeResponse>();
        foreach (var r in _recipes.GetAll()
                     .Where(IsCurrentlyOffered)
                     .OrderBy(r => r.Category)
                     .ThenBy(r => r.GoldCost))
        {
            rows.Add(BuildRow(r, holdings, gold));
        }

        return new CraftCatalogueResponse { Recipes = rows, PlayerGold = gold };
    }

    /// <summary>
    /// D-018 gating: core recipes are always visible; event/guild recipes only while their window is
    /// open. There is no event-window store yet, so an event-keyed recipe is hidden rather than
    /// shown-and-broken — hiding is the safe default, and it keeps the catalogue honest.
    /// </summary>
    private static bool IsCurrentlyOffered(CraftingRecipe r) => r.EventKey is null;

    private sealed record Holdings(
        Dictionary<string, int> Items,
        HashSet<string> Units,
        HashSet<string> Legions,
        Dictionary<string, int> Gear,
        Dictionary<string, string> EquippedUnits);

    private async Task<Holdings> LoadHoldingsAsync(Guid playerId, CancellationToken ct)
    {
        var items = (await _inventory.GetAllForPlayerAsync(playerId, ct))
            .Where(i => i.Quantity > 0)
            .ToDictionary(i => i.ItemDefinitionId, i => i.Quantity, StringComparer.Ordinal);

        var units = (await _units.GetOwnedAsync(playerId, ct))
            .Select(u => u.UnitDefinitionId)
            .ToHashSet(StringComparer.Ordinal);

        var legions = (await _legions.GetOwnedAsync(playerId, ct))
            .Select(l => l.LegionDefinitionId)
            .ToHashSet(StringComparer.Ordinal);

        // Gear stacks, so sum quantities rather than counting rows.
        var gear = (await _gear.GetOwnedAsync(playerId, ct))
            .GroupBy(g => g.GearDefinitionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity), StringComparer.Ordinal);

        // Consuming a slotted unit would dangle PlayerLegionSlot.UnitDefinitionId (D-018's integrity
        // constraint), so the catalogue surfaces that BEFORE the player commits to a craft. The slot
        // repository is per-legion, so this walks the legions the player owns — a small number, and
        // only on a catalogue read.
        //
        // NOTE: the Gauntlet battalion ALSO references units (by id, inside its JSON loadout). Slice 2
        // must check it too when it enforces the rule authoritatively; this display-side check covers
        // legion slots only, so CanCraft here is advisory — the craft call is the authority.
        var equipped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var legionId in legions)
        {
            foreach (var slot in await _slots.GetForLegionAsync(playerId, legionId, ct))
            {
                if (string.IsNullOrEmpty(slot.UnitDefinitionId)) continue;
                var legionName = _legionDefs.GetById(legionId)?.Name ?? legionId;
                equipped.TryAdd(slot.UnitDefinitionId, $"slotted in {legionName}");
            }
        }

        return new Holdings(items, units, legions, gear, equipped);
    }

    private CraftRecipeResponse BuildRow(CraftingRecipe r, Holdings h, long gold)
    {
        var ingredients = new List<CraftIngredientResponse>(r.Ingredients.Count);
        foreach (var ing in r.Ingredients)
        {
            var (name, rarity) = DescribeIngredient(ing);
            int owned = ing.Kind switch
            {
                CraftIngredientKind.Item   => h.Items.GetValueOrDefault(ing.Id),
                CraftIngredientKind.Gear   => h.Gear.GetValueOrDefault(ing.Id),
                // Units and legions are own-once: owned is 1 or 0.
                CraftIngredientKind.Unit   => h.Units.Contains(ing.Id) ? 1 : 0,
                CraftIngredientKind.Legion => h.Legions.Contains(ing.Id) ? 1 : 0,
                _ => 0,
            };

            var blocked = ing.Kind == CraftIngredientKind.Unit && h.EquippedUnits.TryGetValue(ing.Id, out var where)
                ? where
                : null;

            ingredients.Add(new CraftIngredientResponse
            {
                Kind      = ing.Kind.ToString(),
                Id        = ing.Id,
                Name      = name,
                Rarity    = rarity,
                Required  = ing.Quantity,
                Owned     = owned,
                Satisfied = owned >= ing.Quantity && blocked is null,
                BlockedBecauseEquipped = blocked,
            });
        }

        var (outName, outRarity) = DescribeOutput(r);
        bool alreadyOwned = r.OutputKind switch
        {
            CraftOutputKind.Unit   => h.Units.Contains(r.OutputId),
            CraftOutputKind.Legion => h.Legions.Contains(r.OutputId),
            _ => false,   // gear stacks, so owning one never blocks crafting another
        };

        string? blockedReason = null;
        if (alreadyOwned)
            blockedReason = $"You already own {outName}.";
        else if (ingredients.Any(i => i.BlockedBecauseEquipped is not null))
            blockedReason = "An ingredient is equipped — unequip it first.";
        else if (ingredients.Any(i => !i.Satisfied))
            blockedReason = "Missing ingredients.";
        else if (gold < r.GoldCost)
            blockedReason = "Not enough gold.";

        return new CraftRecipeResponse
        {
            RecipeId       = r.Id,
            Name           = r.Name,
            Description    = r.Description,
            Category       = r.Category.ToString(),
            OutputKind     = r.OutputKind.ToString(),
            OutputId       = r.OutputId,
            OutputName     = outName,
            OutputRarity   = outRarity,
            OutputQuantity = r.OutputQuantity,
            Ingredients    = ingredients,
            GoldCost       = r.GoldCost,
            CanCraft       = blockedReason is null,
            BlockedReason  = blockedReason,
            AlreadyOwned   = alreadyOwned,
        };
    }

    private (string Name, string Rarity) DescribeIngredient(CraftIngredient ing) => ing.Kind switch
    {
        CraftIngredientKind.Item   => Describe(_itemDefs.GetById(ing.Id)?.Name,   _itemDefs.GetById(ing.Id)?.Rarity),
        CraftIngredientKind.Unit   => Describe(_unitDefs.GetById(ing.Id)?.Name,   _unitDefs.GetById(ing.Id)?.Rarity),
        CraftIngredientKind.Legion => Describe(_legionDefs.GetById(ing.Id)?.Name, _legionDefs.GetById(ing.Id)?.Rarity),
        CraftIngredientKind.Gear   => Describe(_gearDefs.GetById(ing.Id)?.Name,   _gearDefs.GetById(ing.Id)?.Rarity),
        _ => (ing.Id, string.Empty),
    };

    private (string Name, string Rarity) DescribeOutput(CraftingRecipe r) => r.OutputKind switch
    {
        CraftOutputKind.Unit   => Describe(_unitDefs.GetById(r.OutputId)?.Name,   _unitDefs.GetById(r.OutputId)?.Rarity),
        CraftOutputKind.Legion => Describe(_legionDefs.GetById(r.OutputId)?.Name, _legionDefs.GetById(r.OutputId)?.Rarity),
        CraftOutputKind.Gear   => Describe(_gearDefs.GetById(r.OutputId)?.Name,   _gearDefs.GetById(r.OutputId)?.Rarity),
        _ => (r.OutputId, string.Empty),
    };

    // Boot validation guarantees these resolve; the fallback exists so a content edit that slips
    // through degrades to showing the id rather than throwing at a player.
    private static (string, string) Describe(string? name, ItemRarity? rarity)
        => (name ?? "(unknown)", rarity?.ToString() ?? string.Empty);
}
