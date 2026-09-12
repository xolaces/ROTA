using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class GearDefinition
{
    public string      Id          { get; set; } = string.Empty;
    public string      Name        { get; set; } = string.Empty;
    public string      Description { get; set; } = string.Empty;
    public ItemRarity  Rarity      { get; set; }
    public string      Slot        { get; set; } = string.Empty; // stored as string, parsed to EquipmentSlot
    public int         BonusAttack  { get; set; }
    public int         BonusDefense { get; set; }
    public double?     ProcChance   { get; set; } // null = no proc
    public double?     ProcPercent  { get; set; } // bonus added = baseDamage × ProcPercent
    public string      IconPath     { get; set; } = string.Empty;
    public List<ConditionalBonus> ConditionalBonuses { get; set; } = new();

    // Discernment drop-quality. Next-tier-up gear this can upgrade into;
    // null = never. Validated at startup (resolves + strictly higher rarity ≤ Orange). Field is present +
    // validated now; the gear-drop upgrade roll is wired with the deferred raid-threshold loot work.
    public string? UpgradesTo { get; set; }

    /// <summary>
    /// Which SET this piece belongs to, or null for a piece that stands alone (the Armory relics are
    /// deliberately setless — they do not match and were never meant to be worn together).
    ///
    /// Descriptive ONLY today. Gear set bonuses are still PHASE-2, so nothing reads this in combat;
    /// it exists so the sets are machine-readable BEFORE the bonus mechanic lands, which is the
    /// difference between tuning a set later and re-authoring one. Boot validation checks that every
    /// piece in a set shares a rarity and that no set claims a slot twice — the two mistakes that
    /// make a set impossible to wear.
    /// </summary>
    public string? SetId { get; set; }

    /// <summary>
    /// For a reforged twin, the id of the base piece it was made from; null for everything else.
    /// Written by tools/content/reforge_sets.py. The client uses it to draw a reforged piece with
    /// its base piece's art and mark it as reforged; nothing in combat reads it.
    /// </summary>
    public string? ReforgedFrom { get; set; }
}
