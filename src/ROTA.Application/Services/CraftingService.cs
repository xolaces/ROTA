using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// System 26 — crafting (D-018). Dawn-faithful: a recipe names exact ingredients and produces a
/// better-named DEFINITION (<c>Bucket Brigade II ← Riot II + Bucket Brigade</c>). No levelling — all
/// power lives in the output's own definition.
///
/// Slice 1 was the read-only catalogue. Slice 2 adds <see cref="CraftAsync"/>: verify, charge, consume
/// and grant inside ONE transaction under the per-player mutation lock, so a craft either happens
/// completely or not at all. The catalogue and the craft share <see cref="LoadHoldingsAsync"/> and
/// <see cref="Evaluate"/>, so the button's disabled state and the server's refusal cannot drift apart.
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
    private readonly IGauntletBattalionRepository _battalion;
    private readonly IPlayerEquipmentRepository _equipped;
    private readonly IPlayerCommanderGearRepository _commanderGear;
    private readonly IPlayerMutationLock _mutationLock;
    private readonly IAuditLogRepository _auditLog;

    // Grants go through the owning services rather than straight to the repositories: GrantGearAsync
    // also recounts the EquipmentPiecesOwned achievement, and routing every acquisition through one
    // surface means side-effects added there later apply to crafted items too.
    private readonly ILegionService _legionSvc;
    private readonly IEquipmentService _equipmentSvc;

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
        IPlayerRepository players,
        IGauntletBattalionRepository battalion,
        IPlayerEquipmentRepository equipped,
        IPlayerCommanderGearRepository commanderGear,
        IPlayerMutationLock mutationLock,
        IAuditLogRepository auditLog,
        ILegionService legionSvc,
        IEquipmentService equipmentSvc)
    {
        _recipes       = recipes;
        _itemDefs      = itemDefs;
        _unitDefs      = unitDefs;
        _legionDefs    = legionDefs;
        _gearDefs      = gearDefs;
        _inventory     = inventory;
        _units         = units;
        _legions       = legions;
        _gear          = gear;
        _slots         = slots;
        _players       = players;
        _battalion     = battalion;
        _equipped      = equipped;
        _commanderGear = commanderGear;
        _mutationLock  = mutationLock;
        _auditLog      = auditLog;
        _legionSvc     = legionSvc;
        _equipmentSvc  = equipmentSvc;
    }

    // ---------------------------------------------------------------- catalogue

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

    // ---------------------------------------------------------------- craft

    public Task<CraftResponse> CraftAsync(Guid playerId, string recipeId, CancellationToken ct = default)
    {
        // Cheap rejects before taking the lock — neither depends on player state.
        var recipe = _recipes.GetById(recipeId ?? string.Empty);
        if (recipe is null)
            return Task.FromResult(Fail(CraftFailureCode.RecipeNotFound, "No such recipe."));
        if (!IsCurrentlyOffered(recipe))
            return Task.FromResult(Fail(CraftFailureCode.RecipeNotAvailable,
                "That recipe is not currently offered."));

        return _mutationLock.RunAsync(playerId, () => CraftCoreAsync(playerId, recipe, ct), ct);
    }

    /// <summary>
    /// Runs inside the mutation lock's transaction. Everything it reads is committed truth, and
    /// everything it writes commits together — so two concurrent crafts of the same recipe cannot both
    /// pass the ingredient check, and a failure after the gold debit cannot leave the player poorer.
    /// </summary>
    private async Task<CraftResponse> CraftCoreAsync(
        Guid playerId, CraftingRecipe recipe, CancellationToken ct)
    {
        var h = await LoadHoldingsAsync(playerId, ct);

        // Own-once outputs: granting a second copy is a silent no-op that would still have eaten the
        // ingredients, so refuse before anything is charged.
        var (outName, _) = DescribeOutput(recipe);
        if (IsOwnOnce(recipe.OutputKind) && OwnsOutput(recipe, h))
            return Fail(CraftFailureCode.AlreadyOwned, $"You already own {outName}.");

        foreach (var ing in recipe.Ingredients)
        {
            var (owned, blocked) = Evaluate(ing, h);
            var (ingName, _) = DescribeIngredient(ing);

            if (blocked is not null)
                return Fail(CraftFailureCode.IngredientInUse, $"{ingName} is {blocked}.");
            if (owned < ing.Quantity)
                return Fail(CraftFailureCode.MissingIngredients,
                    $"You need {ing.Quantity}x {ingName} and hold {owned}.");
        }

        // Gold is a COLUMN, not a ledger, so the debit is a CONDITIONAL UPDATE that re-checks the
        // balance in the same statement — a read-then-write race can never drive it negative. It runs
        // inside this transaction, so it rolls back with everything else if a later step throws.
        long newGold;
        if (recipe.GoldCost > 0)
        {
            var spent = await _players.TrySpendGoldAsync(playerId, recipe.GoldCost, ct);
            if (spent is null)
                return Fail(CraftFailureCode.InsufficientGold,
                    $"Insufficient gold. Required: {recipe.GoldCost}.");
            newGold = spent.Value;
        }
        else
        {
            newGold = (await _players.FindByIdAsync(playerId, ct))?.Gold ?? 0;
        }

        var consumed = new List<CraftConsumedResponse>(recipe.Ingredients.Count);
        int slotsCleared = 0;
        foreach (var ing in recipe.Ingredients)
        {
            slotsCleared += await ConsumeAsync(playerId, ing, ct);
            var (ingName, _) = DescribeIngredient(ing);
            consumed.Add(new CraftConsumedResponse
            {
                Kind     = ing.Kind.ToString(),
                Id       = ing.Id,
                Name     = ingName,
                Quantity = ing.Quantity,
            });
        }

        await GrantOutputAsync(playerId, recipe, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "ItemCrafted", null,
            $"Crafted {recipe.OutputQuantity}x {outName} ({recipe.OutputKind} {recipe.OutputId}) " +
            $"via '{recipe.Id}' for {recipe.GoldCost} gold. Consumed: " +
            string.Join(", ", consumed.Select(c => $"{c.Quantity}x {c.Id}")) +
            $". Gold now {newGold}.", null), ct);

        return new CraftResponse
        {
            Success            = true,
            RecipeId           = recipe.Id,
            OutputKind         = recipe.OutputKind.ToString(),
            OutputId           = recipe.OutputId,
            OutputName         = outName,
            OutputQuantity     = recipe.OutputQuantity,
            Consumed           = consumed,
            GoldSpent          = recipe.GoldCost,
            NewPlayerGold      = newGold,
            LegionSlotsCleared = slotsCleared,
        };
    }

    /// <summary>
    /// Takes one ingredient line away. Returns how many legion slots it freed as a side effect (0 for
    /// every kind but Legion). Throws if the holding is missing — <see cref="CraftCoreAsync"/> has
    /// already re-checked it under the lock, so a miss here is a bug that must roll the craft back
    /// rather than quietly craft from nothing.
    /// </summary>
    private async Task<int> ConsumeAsync(Guid playerId, CraftIngredient ing, CancellationToken ct)
    {
        switch (ing.Kind)
        {
            case CraftIngredientKind.Item:
            {
                var row = await _inventory.GetAsync(playerId, ing.Id, ct)
                    ?? throw new InvalidOperationException($"Craft consume: no inventory row for '{ing.Id}'.");
                if (row.Quantity < ing.Quantity)
                    throw new InvalidOperationException(
                        $"Craft consume: only {row.Quantity}x '{ing.Id}' held, needed {ing.Quantity}.");
                row.ConsumeQuantity(ing.Quantity);
                await _inventory.UpdateAsync(row, ct);
                return 0;
            }

            case CraftIngredientKind.Gear:
            {
                var row = await _gear.GetAsync(playerId, ing.Id, ct)
                    ?? throw new InvalidOperationException($"Craft consume: no gear row for '{ing.Id}'.");
                row.ConsumeQuantity(ing.Quantity);   // throws on a short stack
                await _gear.UpdateAsync(row, ct);
                return 0;
            }

            case CraftIngredientKind.Unit:
            {
                var row = await _units.FindAsync(playerId, ing.Id, ct);
                if (row is null || row.IsDeleted)
                    throw new InvalidOperationException($"Craft consume: unit '{ing.Id}' not owned.");
                row.SoftDelete();
                await _units.UpdateAsync(row, ct);
                return 0;
            }

            case CraftIngredientKind.Legion:
            {
                var row = await _legions.FindAsync(playerId, ing.Id, ct);
                if (row is null || row.IsDeleted)
                    throw new InvalidOperationException($"Craft consume: legion '{ing.Id}' not owned.");

                // A legion's slot rows belong to the legion, not to the player's collection. Dissolving
                // the legion without clearing them would leave PlayerLegionSlot rows pointing at a
                // legion that no longer exists — the dangling reference D-018 forbids. The units
                // themselves are untouched; only the arrangement is lost, and the catalogue warned.
                int cleared = 0;
                foreach (var slot in await _slots.GetForLegionAsync(playerId, ing.Id, ct))
                {
                    await _slots.SoftDeleteAsync(slot, ct);
                    cleared++;
                }

                row.SoftDelete();
                await _legions.UpdateAsync(row, ct);
                return cleared;
            }

            default:
                throw new InvalidOperationException($"Craft consume: unhandled ingredient kind {ing.Kind}.");
        }
    }

    private async Task GrantOutputAsync(Guid playerId, CraftingRecipe r, CancellationToken ct)
    {
        switch (r.OutputKind)
        {
            case CraftOutputKind.Unit:
                await _legionSvc.GrantUnitAsync(playerId, r.OutputId, ct);
                break;
            case CraftOutputKind.Legion:
                // Deliberately NOT made active: a craft changes what you own, never what you have
                // deployed. The player activates the new legion when they are ready.
                await _legionSvc.GrantLegionAsync(playerId, r.OutputId, ct);
                break;
            case CraftOutputKind.Gear:
                await _equipmentSvc.GrantGearAsync(playerId, r.OutputId, r.OutputQuantity, ct);
                break;
            default:
                throw new InvalidOperationException($"Craft grant: unhandled output kind {r.OutputKind}.");
        }
    }

    // ---------------------------------------------------------------- shared state

    /// <summary>
    /// D-018 gating: core recipes are always visible; event/guild recipes only while their window is
    /// open. There is no event-window store yet, so an event-keyed recipe is hidden rather than
    /// shown-and-broken — hiding is the safe default, and it keeps the catalogue honest.
    /// </summary>
    private static bool IsCurrentlyOffered(CraftingRecipe r) => r.EventKey is null;

    private static bool IsOwnOnce(CraftOutputKind kind)
        => kind is CraftOutputKind.Unit or CraftOutputKind.Legion;

    private static bool OwnsOutput(CraftingRecipe r, Holdings h) => r.OutputKind switch
    {
        CraftOutputKind.Unit   => h.Units.Contains(r.OutputId),
        CraftOutputKind.Legion => h.Legions.Contains(r.OutputId),
        _ => false,   // gear stacks, so owning one never blocks crafting another
    };

    /// <param name="UnitInUse">Unit id → where it is referenced, so a refusal can name the place to free it.</param>
    /// <param name="GearEquipped">Gear id → how many copies are currently equipped (commander slot included).</param>
    /// <param name="LegionSlotCounts">Legion id → units slotted into it, for the "this clears your loadout" warning.</param>
    private sealed record Holdings(
        Dictionary<string, int> Items,
        HashSet<string> Units,
        HashSet<string> Legions,
        Dictionary<string, int> Gear,
        Dictionary<string, string> UnitInUse,
        Dictionary<string, int> GearEquipped,
        string? ActiveLegionId,
        Dictionary<string, int> LegionSlotCounts);

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

        // A unit is referenced from TWO places, and consuming one that is still referenced would dangle
        // that reference (D-018's integrity constraint): legion slots, and the Gauntlet battalion's JSON
        // loadout. Slice 1 checked only the former, which is why its CanCraft was advisory. Both are
        // checked here, and this is now the same code the craft call itself runs.
        var inUse = new Dictionary<string, string>(StringComparer.Ordinal);
        var slotCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var legionId in legions)
        {
            var legionName = _legionDefs.GetById(legionId)?.Name ?? legionId;
            int count = 0;
            foreach (var slot in await _slots.GetForLegionAsync(playerId, legionId, ct))
            {
                if (string.IsNullOrEmpty(slot.UnitDefinitionId)) continue;
                count++;
                inUse.TryAdd(slot.UnitDefinitionId, $"slotted in {legionName}");
            }
            slotCounts[legionId] = count;
        }

        var battalion = await _battalion.GetForPlayerAsync(playerId, ct);
        if (battalion is not null && !battalion.IsDeleted)
        {
            foreach (var id in ParseIds(battalion.GeneralsJson).Concat(ParseIds(battalion.TroopsJson)))
                inUse.TryAdd(id, "in your Gauntlet battalion");
        }

        // Equipping never decrements the gear stack, so "owned" and "equipped" overlap: a craft may take
        // gear only while it leaves a copy behind for every slot still wearing it.
        var gearEquipped = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in await _equipped.GetEquippedAsync(playerId, ct))
        {
            if (string.IsNullOrEmpty(e.GearDefinitionId)) continue;
            gearEquipped[e.GearDefinitionId] = gearEquipped.GetValueOrDefault(e.GearDefinitionId) + 1;
        }
        var commander = await _commanderGear.FindAsync(playerId, ct);
        if (commander is not null && !commander.IsDeleted && !string.IsNullOrEmpty(commander.GearDefinitionId))
            gearEquipped[commander.GearDefinitionId] = gearEquipped.GetValueOrDefault(commander.GearDefinitionId) + 1;

        var active = await _legions.GetActiveAsync(playerId, ct);

        return new Holdings(items, units, legions, gear, inUse, gearEquipped,
            active?.LegionDefinitionId, slotCounts);
    }

    /// <summary>
    /// Battalion loadouts are stored as JSON id arrays written by GauntletBattalionService. A malformed
    /// array must not take crafting down, so a parse failure is treated as "no battalion units" — the
    /// same as an empty loadout.
    /// </summary>
    private static IEnumerable<string> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json)
                       ?.Where(s => !string.IsNullOrWhiteSpace(s))
                   ?? Enumerable.Empty<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The single source of truth for "can this ingredient line be taken right now" — used to render the
    /// catalogue AND to authorise the craft, so the two cannot drift apart. Returns how many the player
    /// holds and, when the holding exists but cannot be taken, why.
    /// </summary>
    private static (int Owned, string? Blocked) Evaluate(CraftIngredient ing, Holdings h)
    {
        switch (ing.Kind)
        {
            case CraftIngredientKind.Item:
                return (h.Items.GetValueOrDefault(ing.Id), null);

            case CraftIngredientKind.Gear:
            {
                int owned = h.Gear.GetValueOrDefault(ing.Id);
                int worn  = h.GearEquipped.GetValueOrDefault(ing.Id);
                // Only a craft that would eat into the equipped copies is blocked; spare copies craft
                // freely, which is why this counts rather than treating "equipped" as a flag.
                bool blocked = worn > 0 && owned - ing.Quantity < worn;
                return (owned, blocked
                    ? $"equipped ({worn} in use, {owned} held) — unequip a copy or find another"
                    : null);
            }

            case CraftIngredientKind.Unit:
            {
                int owned = h.Units.Contains(ing.Id) ? 1 : 0;
                return (owned, owned > 0 && h.UnitInUse.TryGetValue(ing.Id, out var where) ? where : null);
            }

            case CraftIngredientKind.Legion:
            {
                int owned = h.Legions.Contains(ing.Id) ? 1 : 0;
                // Dissolving the active legion would leave the player deployed on nothing, so it is
                // refused rather than silently reassigned. Switching active legion is the fix.
                return (owned, owned > 0 && h.ActiveLegionId == ing.Id
                    ? "your active legion — make another legion active first"
                    : null);
            }

            default:
                return (0, null);
        }
    }

    private CraftRecipeResponse BuildRow(CraftingRecipe r, Holdings h, long gold)
    {
        var ingredients = new List<CraftIngredientResponse>(r.Ingredients.Count);
        foreach (var ing in r.Ingredients)
        {
            var (name, rarity) = DescribeIngredient(ing);
            var (owned, blocked) = Evaluate(ing, h);

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
        bool alreadyOwned = IsOwnOnce(r.OutputKind) && OwnsOutput(r, h);

        string? blockedReason = null;
        if (alreadyOwned)
            blockedReason = $"You already own {outName}.";
        else if (ingredients.Any(i => i.BlockedBecauseEquipped is not null))
            blockedReason = "An ingredient is in use — free it first.";
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
            Warning        = BuildWarning(r, h),
        };
    }

    /// <summary>A consequence worth knowing before committing — never a block.</summary>
    private string? BuildWarning(CraftingRecipe r, Holdings h)
    {
        foreach (var ing in r.Ingredients.Where(i => i.Kind == CraftIngredientKind.Legion))
        {
            if (!h.Legions.Contains(ing.Id)) continue;
            int slotted = h.LegionSlotCounts.GetValueOrDefault(ing.Id);
            if (slotted == 0) continue;

            var name = _legionDefs.GetById(ing.Id)?.Name ?? ing.Id;
            return $"This dissolves {name} and clears its {slotted} slotted " +
                   $"{(slotted == 1 ? "unit" : "units")}. The units are kept — the arrangement is not.";
        }
        return null;
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

    private static CraftResponse Fail(CraftFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
