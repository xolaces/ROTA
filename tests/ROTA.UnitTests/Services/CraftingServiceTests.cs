using System.Text.Json;
using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;
using ROTA.UnitTests.TestSupport;

namespace ROTA.UnitTests.Services;

/// <summary>
/// System 26 slice 2 (D-018) — the consuming transaction. These cover the refusal rules, that a refusal
/// costs the player nothing, and that a success takes exactly what the recipe named.
/// </summary>
public class CraftingServiceTests
{
    private static readonly Guid PlayerId = Guid.NewGuid();

    // Ironward II ← Ironward + 2 oathsteel + 10 iron shard, 15,000 gold. Mirrors the shipped recipe.
    private const string RecipeId = "craft_ironward_ii";
    private const string InUnit   = "gen_ironward";
    private const string OutUnit  = "gen_ironward_ii";
    private const string MatA     = "mat_oathsteel";
    private const string MatB     = "mat_iron_shard";

    // HELPERS

    private sealed record Bundle(
        CraftingService Service,
        Mock<ICraftingRecipeProvider> Recipes,
        Mock<IPlayerInventoryRepository> Inventory,
        Mock<IPlayerUnitRepository> Units,
        Mock<IPlayerLegionRepository> Legions,
        Mock<IPlayerGearRepository> Gear,
        Mock<IPlayerLegionSlotRepository> Slots,
        Mock<IPlayerRepository> Players,
        Mock<IGauntletBattalionRepository> Battalion,
        Mock<IPlayerEquipmentRepository> Equipped,
        Mock<IPlayerCommanderGearRepository> CommanderGear,
        Mock<IAuditLogRepository> AuditLog,
        Mock<ILegionService> LegionSvc,
        Mock<IEquipmentService> EquipmentSvc);

    private static CraftingRecipe UnitRecipe(long goldCost = 15_000) => new()
    {
        Id = RecipeId,
        Name = "Ironward the Unbroken",
        Category = CraftRecipeCategory.General,
        OutputKind = CraftOutputKind.Unit,
        OutputId = OutUnit,
        OutputQuantity = 1,
        GoldCost = goldCost,
        Ingredients =
        [
            new CraftIngredient { Kind = CraftIngredientKind.Unit, Id = InUnit,  Quantity = 1  },
            new CraftIngredient { Kind = CraftIngredientKind.Item, Id = MatA,    Quantity = 2  },
            new CraftIngredient { Kind = CraftIngredientKind.Item, Id = MatB,    Quantity = 10 },
        ],
    };

    private static Bundle BuildService(CraftingRecipe recipe)
    {
        var recipes       = new Mock<ICraftingRecipeProvider>();
        var itemDefs      = new Mock<IItemDefinitionProvider>();
        var unitDefs      = new Mock<IUnitDefinitionProvider>();
        var legionDefs    = new Mock<ILegionDefinitionProvider>();
        var gearDefs      = new Mock<IGearDefinitionProvider>();
        var inventory     = new Mock<IPlayerInventoryRepository>();
        var units         = new Mock<IPlayerUnitRepository>();
        var legions       = new Mock<IPlayerLegionRepository>();
        var gear          = new Mock<IPlayerGearRepository>();
        var slots         = new Mock<IPlayerLegionSlotRepository>();
        var players       = new Mock<IPlayerRepository>();
        var battalion     = new Mock<IGauntletBattalionRepository>();
        var equipped      = new Mock<IPlayerEquipmentRepository>();
        var commanderGear = new Mock<IPlayerCommanderGearRepository>();
        var auditLog      = new Mock<IAuditLogRepository>();
        var legionSvc     = new Mock<ILegionService>();
        var equipmentSvc  = new Mock<IEquipmentService>();

        recipes.Setup(r => r.GetById(recipe.Id)).Returns(recipe);
        recipes.Setup(r => r.GetAll()).Returns([recipe]);

        // Names resolve for every id these tests touch, so failure messages read like production's.
        itemDefs.Setup(d => d.GetById(It.IsAny<string>()))
            .Returns((string id) => new ItemDefinition { Id = id, Name = id, Rarity = ItemRarity.Green });
        unitDefs.Setup(d => d.GetById(It.IsAny<string>()))
            .Returns((string id) => new UnitDefinition { Id = id, Name = id, Rarity = ItemRarity.Blue });
        legionDefs.Setup(d => d.GetById(It.IsAny<string>()))
            .Returns((string id) => new LegionDefinition { Id = id, Name = id, Rarity = ItemRarity.Blue });
        gearDefs.Setup(d => d.GetById(It.IsAny<string>()))
            .Returns((string id) => new GearDefinition { Id = id, Name = id, Rarity = ItemRarity.Green });

        // Empty-by-default holdings; each test fills in only what it cares about.
        inventory.Setup(r => r.GetAllForPlayerAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        inventory.Setup(r => r.UpdateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        units.Setup(r => r.GetOwnedAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        units.Setup(r => r.UpdateAsync(It.IsAny<PlayerUnit>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        legions.Setup(r => r.GetOwnedAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        legions.Setup(r => r.GetActiveAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerLegion?)null);
        legions.Setup(r => r.UpdateAsync(It.IsAny<PlayerLegion>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        gear.Setup(r => r.GetOwnedAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        gear.Setup(r => r.UpdateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        slots.Setup(r => r.GetForLegionAsync(PlayerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        slots.Setup(r => r.SoftDeleteAsync(It.IsAny<PlayerLegionSlot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        battalion.Setup(r => r.GetForPlayerAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerGauntletBattalion?)null);
        equipped.Setup(r => r.GetEquippedAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        commanderGear.Setup(r => r.FindAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerCommanderGear?)null);
        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Affordable by default; the insufficient-gold test overrides this with null.
        players.Setup(r => r.TrySpendGoldAsync(PlayerId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1_000L);

        var svc = new CraftingService(
            recipes.Object, itemDefs.Object, unitDefs.Object, legionDefs.Object, gearDefs.Object,
            inventory.Object, units.Object, legions.Object, gear.Object, slots.Object, players.Object,
            battalion.Object, equipped.Object, commanderGear.Object,
            new PassThroughPlayerMutationLock(), auditLog.Object,
            legionSvc.Object, equipmentSvc.Object);

        return new Bundle(svc, recipes, inventory, units, legions, gear, slots, players,
            battalion, equipped, commanderGear, auditLog, legionSvc, equipmentSvc);
    }

    /// <summary>Gives the player everything <see cref="UnitRecipe"/> asks for.</summary>
    private static void GiveFullIngredients(Bundle b)
    {
        var owned = PlayerUnit.Create(PlayerId, InUnit);
        b.Units.Setup(r => r.GetOwnedAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([owned]);
        b.Units.Setup(r => r.FindAsync(PlayerId, InUnit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owned);

        var matA = PlayerInventoryItem.Create(PlayerId, MatA, 5);
        var matB = PlayerInventoryItem.Create(PlayerId, MatB, 20);
        b.Inventory.Setup(r => r.GetAllForPlayerAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matA, matB]);
        b.Inventory.Setup(r => r.GetAsync(PlayerId, MatA, It.IsAny<CancellationToken>())).ReturnsAsync(matA);
        b.Inventory.Setup(r => r.GetAsync(PlayerId, MatB, It.IsAny<CancellationToken>())).ReturnsAsync(matB);
    }

    private static PlayerGauntletBattalion BattalionWith(params string[] generals)
    {
        var bat = PlayerGauntletBattalion.Create(PlayerId);
        bat.SetLoadout(JsonSerializer.Serialize(generals), "[]");
        return bat;
    }

    // HAPPY PATH

    [Fact]
    public async Task CraftAsync_consumes_every_ingredient_charges_gold_and_grants_the_output()
    {
        var b = BuildService(UnitRecipe());
        GiveFullIngredients(b);

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.Success.Should().BeTrue();
        result.FailureCode.Should().Be(CraftFailureCode.None);
        result.OutputId.Should().Be(OutUnit);
        result.GoldSpent.Should().Be(15_000);
        result.NewPlayerGold.Should().Be(1_000);
        result.Consumed.Should().HaveCount(3);
        result.LegionSlotsCleared.Should().Be(0);

        b.Players.Verify(r => r.TrySpendGoldAsync(PlayerId, 15_000, It.IsAny<CancellationToken>()), Times.Once);
        b.LegionSvc.Verify(s => s.GrantUnitAsync(PlayerId, OutUnit, It.IsAny<CancellationToken>()), Times.Once);
        // The consumed unit is soft-deleted, not left owned alongside its upgrade.
        b.Units.Verify(r => r.UpdateAsync(It.Is<PlayerUnit>(u => u.UnitDefinitionId == InUnit && u.IsDeleted),
            It.IsAny<CancellationToken>()), Times.Once);
        b.Inventory.Verify(r => r.UpdateAsync(It.Is<PlayerInventoryItem>(i => i.ItemDefinitionId == MatA && i.Quantity == 3),
            It.IsAny<CancellationToken>()), Times.Once);
        b.Inventory.Verify(r => r.UpdateAsync(It.Is<PlayerInventoryItem>(i => i.ItemDefinitionId == MatB && i.Quantity == 10),
            It.IsAny<CancellationToken>()), Times.Once);
        b.AuditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(l => l.Action == "ItemCrafted"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CraftAsync_with_a_free_recipe_does_not_touch_the_gold_column()
    {
        var b = BuildService(UnitRecipe(goldCost: 0));
        GiveFullIngredients(b);
        b.Players.Setup(r => r.FindByIdAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.Success.Should().BeTrue();
        result.GoldSpent.Should().Be(0);
        b.Players.Verify(r => r.TrySpendGoldAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // REFUSALS — each must leave the player exactly as they were

    [Fact]
    public async Task CraftAsync_rejects_an_unknown_recipe()
    {
        var b = BuildService(UnitRecipe());
        b.Recipes.Setup(r => r.GetById("nope")).Returns((CraftingRecipe?)null);

        var result = await b.Service.CraftAsync(PlayerId, "nope");

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CraftFailureCode.RecipeNotFound);
    }

    [Fact]
    public async Task CraftAsync_hides_a_recipe_whose_event_window_is_closed()
    {
        var recipe = UnitRecipe();
        recipe.EventKey = "gauntlet_run_7";
        var b = BuildService(recipe);
        GiveFullIngredients(b);

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.FailureCode.Should().Be(CraftFailureCode.RecipeNotAvailable);
        b.Players.Verify(r => r.TrySpendGoldAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CraftAsync_refuses_when_a_material_is_short_and_charges_nothing()
    {
        var b = BuildService(UnitRecipe());
        GiveFullIngredients(b);
        // One oathsteel short of the two the recipe names.
        var short1 = PlayerInventoryItem.Create(PlayerId, MatA, 1);
        var matB   = PlayerInventoryItem.Create(PlayerId, MatB, 20);
        b.Inventory.Setup(r => r.GetAllForPlayerAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([short1, matB]);

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CraftFailureCode.MissingIngredients);
        result.FailureReason.Should().Contain(MatA);
        b.Players.Verify(r => r.TrySpendGoldAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        b.Units.Verify(r => r.UpdateAsync(It.IsAny<PlayerUnit>(), It.IsAny<CancellationToken>()), Times.Never);
        b.LegionSvc.Verify(s => s.GrantUnitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CraftAsync_refuses_a_unit_that_is_slotted_in_a_legion()
    {
        var b = BuildService(UnitRecipe());
        GiveFullIngredients(b);
        b.Legions.Setup(r => r.GetOwnedAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([PlayerLegion.Create(PlayerId, "legion_vanguard")]);
        b.Slots.Setup(r => r.GetForLegionAsync(PlayerId, "legion_vanguard", It.IsAny<CancellationToken>()))
            .ReturnsAsync([PlayerLegionSlot.Create(PlayerId, "legion_vanguard", LegionSlotFamily.General, 0, InUnit)]);

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.FailureCode.Should().Be(CraftFailureCode.IngredientInUse);
        result.FailureReason.Should().Contain("slotted in");
        b.Units.Verify(r => r.UpdateAsync(It.IsAny<PlayerUnit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The slice-2 requirement slice 1 could not enforce: a unit is also referenced from the Gauntlet
    /// battalion's JSON loadout, and consuming it there would dangle that reference just as a legion
    /// slot would.
    /// </summary>
    [Fact]
    public async Task CraftAsync_refuses_a_unit_that_is_in_the_gauntlet_battalion()
    {
        var b = BuildService(UnitRecipe());
        GiveFullIngredients(b);
        b.Battalion.Setup(r => r.GetForPlayerAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BattalionWith(InUnit));

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.FailureCode.Should().Be(CraftFailureCode.IngredientInUse);
        result.FailureReason.Should().Contain("Gauntlet battalion");
        b.Units.Verify(r => r.UpdateAsync(It.IsAny<PlayerUnit>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Players.Verify(r => r.TrySpendGoldAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CraftAsync_ignores_a_battalion_holding_unrelated_units()
    {
        var b = BuildService(UnitRecipe());
        GiveFullIngredients(b);
        b.Battalion.Setup(r => r.GetForPlayerAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BattalionWith("gen_someone_else"));

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CraftAsync_refuses_when_the_own_once_output_is_already_held()
    {
        var b = BuildService(UnitRecipe());
        GiveFullIngredients(b);
        b.Units.Setup(r => r.GetOwnedAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([PlayerUnit.Create(PlayerId, InUnit), PlayerUnit.Create(PlayerId, OutUnit)]);

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.FailureCode.Should().Be(CraftFailureCode.AlreadyOwned);
        b.Players.Verify(r => r.TrySpendGoldAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        b.Units.Verify(r => r.UpdateAsync(It.IsAny<PlayerUnit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CraftAsync_refuses_on_insufficient_gold_before_consuming_anything()
    {
        var b = BuildService(UnitRecipe());
        GiveFullIngredients(b);
        b.Players.Setup(r => r.TrySpendGoldAsync(PlayerId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var result = await b.Service.CraftAsync(PlayerId, RecipeId);

        result.FailureCode.Should().Be(CraftFailureCode.InsufficientGold);
        b.Units.Verify(r => r.UpdateAsync(It.IsAny<PlayerUnit>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Inventory.Verify(r => r.UpdateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        b.LegionSvc.Verify(s => s.GrantUnitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // GEAR — equipping does not decrement the stack, so the rule counts copies rather than flagging

    private static CraftingRecipe GearRecipe() => new()
    {
        Id = "craft_oathsteel_helm",
        Name = "Oathsteel Helm",
        Category = CraftRecipeCategory.General,
        OutputKind = CraftOutputKind.Gear,
        OutputId = "gear_oathsteel_helm",
        OutputQuantity = 1,
        GoldCost = 0,
        Ingredients = [new CraftIngredient { Kind = CraftIngredientKind.Gear, Id = "gear_conscript_helm", Quantity = 1 }],
    };

    private static void GiveGear(Bundle b, int quantity)
    {
        var row = PlayerGear.Create(PlayerId, "gear_conscript_helm", quantity);
        b.Gear.Setup(r => r.GetOwnedAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync([row]);
        b.Gear.Setup(r => r.GetAsync(PlayerId, "gear_conscript_helm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    [Fact]
    public async Task CraftAsync_refuses_gear_when_consuming_it_would_strip_an_equipped_slot()
    {
        var b = BuildService(GearRecipe());
        GiveGear(b, quantity: 1);
        b.Equipped.Setup(r => r.GetEquippedAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([PlayerEquipment.Create(PlayerId, EquipmentSlot.Head, "gear_conscript_helm")]);

        var result = await b.Service.CraftAsync(PlayerId, "craft_oathsteel_helm");

        result.FailureCode.Should().Be(CraftFailureCode.IngredientInUse);
        b.Gear.Verify(r => r.UpdateAsync(It.IsAny<PlayerGear>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CraftAsync_allows_gear_that_is_equipped_when_a_spare_copy_remains()
    {
        var b = BuildService(GearRecipe());
        GiveGear(b, quantity: 2);
        b.Equipped.Setup(r => r.GetEquippedAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([PlayerEquipment.Create(PlayerId, EquipmentSlot.Head, "gear_conscript_helm")]);
        b.Players.Setup(r => r.FindByIdAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        var result = await b.Service.CraftAsync(PlayerId, "craft_oathsteel_helm");

        result.Success.Should().BeTrue();
        b.Gear.Verify(r => r.UpdateAsync(It.Is<PlayerGear>(g => g.Quantity == 1), It.IsAny<CancellationToken>()),
            Times.Once);
        b.EquipmentSvc.Verify(s => s.GrantGearAsync(PlayerId, "gear_oathsteel_helm", 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CraftAsync_counts_the_commander_slot_as_an_equipped_copy()
    {
        var b = BuildService(GearRecipe());
        GiveGear(b, quantity: 1);
        b.CommanderGear.Setup(r => r.FindAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommanderGear.Create(PlayerId, "gear_conscript_helm"));

        var result = await b.Service.CraftAsync(PlayerId, "craft_oathsteel_helm");

        result.FailureCode.Should().Be(CraftFailureCode.IngredientInUse);
    }

    // LEGIONS — dissolving one clears its slot rows so nothing dangles

    private static CraftingRecipe LegionRecipe() => new()
    {
        Id = "craft_vanguard_ii",
        Name = "Dawn Vanguard II",
        Category = CraftRecipeCategory.General,
        OutputKind = CraftOutputKind.Legion,
        OutputId = "legion_vanguard_ii",
        OutputQuantity = 1,
        GoldCost = 0,
        Ingredients = [new CraftIngredient { Kind = CraftIngredientKind.Legion, Id = "legion_vanguard", Quantity = 1 }],
    };

    private static void GiveLegion(Bundle b, bool active, int slottedUnits)
    {
        var legion = PlayerLegion.Create(PlayerId, "legion_vanguard");
        if (active) legion.SetActive(true);
        b.Legions.Setup(r => r.GetOwnedAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync([legion]);
        b.Legions.Setup(r => r.FindAsync(PlayerId, "legion_vanguard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(legion);
        b.Legions.Setup(r => r.GetActiveAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(active ? legion : null);

        var slots = Enumerable.Range(0, slottedUnits)
            .Select(i => PlayerLegionSlot.Create(PlayerId, "legion_vanguard", LegionSlotFamily.Troop, i, $"troop_{i}"))
            .ToList();
        b.Slots.Setup(r => r.GetForLegionAsync(PlayerId, "legion_vanguard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(slots);
        b.Players.Setup(r => r.FindByIdAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);
    }

    [Fact]
    public async Task CraftAsync_refuses_to_dissolve_the_active_legion()
    {
        var b = BuildService(LegionRecipe());
        GiveLegion(b, active: true, slottedUnits: 0);

        var result = await b.Service.CraftAsync(PlayerId, "craft_vanguard_ii");

        result.FailureCode.Should().Be(CraftFailureCode.IngredientInUse);
        result.FailureReason.Should().Contain("active legion");
        b.Legions.Verify(r => r.UpdateAsync(It.IsAny<PlayerLegion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CraftAsync_clears_the_consumed_legions_slots_so_none_dangle()
    {
        var b = BuildService(LegionRecipe());
        GiveLegion(b, active: false, slottedUnits: 3);

        var result = await b.Service.CraftAsync(PlayerId, "craft_vanguard_ii");

        result.Success.Should().BeTrue();
        result.LegionSlotsCleared.Should().Be(3);
        b.Slots.Verify(r => r.SoftDeleteAsync(It.IsAny<PlayerLegionSlot>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        b.Legions.Verify(r => r.UpdateAsync(It.Is<PlayerLegion>(l => l.IsDeleted), It.IsAny<CancellationToken>()),
            Times.Once);
        b.LegionSvc.Verify(s => s.GrantLegionAsync(PlayerId, "legion_vanguard_ii", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // CATALOGUE — the button's state must agree with what the craft call would actually do

    [Fact]
    public async Task GetCatalogueAsync_blocks_a_recipe_whose_unit_sits_in_the_battalion()
    {
        var b = BuildService(UnitRecipe());
        GiveFullIngredients(b);
        b.Battalion.Setup(r => r.GetForPlayerAsync(PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BattalionWith(InUnit));
        b.Players.Setup(r => r.FindByIdAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var catalogue = await b.Service.GetCatalogueAsync(PlayerId);

        var row = catalogue.Recipes.Single();
        row.CanCraft.Should().BeFalse();
        row.Ingredients.Single(i => i.Id == InUnit).BlockedBecauseEquipped.Should().Contain("Gauntlet battalion");
    }

    [Fact]
    public async Task GetCatalogueAsync_warns_that_dissolving_a_legion_clears_its_loadout()
    {
        var b = BuildService(LegionRecipe());
        GiveLegion(b, active: false, slottedUnits: 4);

        var catalogue = await b.Service.GetCatalogueAsync(PlayerId);

        var row = catalogue.Recipes.Single();
        row.CanCraft.Should().BeTrue();          // a warning is never a block
        row.Warning.Should().Contain("4 slotted units");
    }
}
