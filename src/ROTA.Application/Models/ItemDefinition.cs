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
}
