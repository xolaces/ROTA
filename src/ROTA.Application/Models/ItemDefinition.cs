using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class ItemDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ItemRarity Rarity { get; set; }
    public ItemType Type { get; set; }
    public string ArtKey { get; set; } = string.Empty;
    public int StatPointsOnUse { get; set; }
    public bool IsCraftingIngredient { get; set; }
    public string? SummonRaidId { get; set; }
    public string? SummonDifficulty { get; set; }  // stored as string, parsed to RaidDifficulty at use time
    public string? SummonSize { get; set; }         // stored as string, parsed to RaidSize at use time; null → Personal
    public List<string> Tags { get; set; } = new(); // used by OwnedTypeCount conditional bonus lookups

    // System 22 Phase A (Slice 7) — Discernment drop-quality. The next-tier-up item this can upgrade
    // into on a successful Discernment-scaled roll at drop-resolution; null = never upgrades. Validated
    // at startup to resolve + be strictly higher rarity (≤ Orange).
    public string? UpgradesTo { get; set; }

    // ── Consumables (D-008 / D-013) — the northstar §1 escape valve ─────────────────────────────
    // BETA: a Consumable restores a resource pool so a willing player is never dead-walled. Parsed to
    // ResourceType at use time (same string-in-content pattern as SummonDifficulty/SummonSize).
    // Validated at startup: a Consumable MUST name a parseable resource and MUST restore something.
    public string? RestoreResourceType { get; set; }

    /// <summary>Points restored per unit used. Ignored when <see cref="RestoreToMax"/> is set.</summary>
    public int RestoreAmount { get; set; }

    /// <summary>Full-pool refill (the premium tier). Quantity is capped to 1 — a second one would be wasted.</summary>
    public bool RestoreToMax { get; set; }

    /// <summary>
    /// Gold cost in the consumable shop. 0 = not purchasable (drop-only). D-013: potions price in GOLD
    /// (gold's only recurring sink); gem-priced instant refills are a separate path, not an item.
    /// </summary>
    public long GoldPrice { get; set; }
}
