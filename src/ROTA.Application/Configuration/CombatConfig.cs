namespace ROTA.Application.Configuration;

public class CombatConfig
{
    public double BaseCritChance { get; set; } = 0.05;
    public double MaxCritChanceBonus { get; set; } = 0.10;
    public double CritChancePerDiscernment { get; set; } = 0.0001;
    public double BaseCritMultiplier { get; set; } = 1.5;
    public double MaxCritDamageBonus { get; set; } = 1.0;
    public double CritDamagePerDiscernment { get; set; } = 0.0002;

    // On-hit raid XP = summed Uniform[XpPerStaminaRollMin, XpPerStaminaRollMax] per stamina spent
    // (ResourceReward.RollSummed). Dawn-faithful (owner 2026-06-15): raids are the PREMIUM leveling path —
    // 1-5 XP/stamina (avg 3.0), ~2× the quest energy rate (QuestConfig.XpPerEnergyRoll* = 1.5 fixed). The
    // higher per-point XP offsets stamina's 2× LSI build cost, and raids also drop the gear/gold/loot — but
    // they're gated by combat power. ⇒ avg ~60 on a 20-stamina hit (range 20-100).
    public double XpPerStaminaRollMin { get; set; } = 1.0;
    public double XpPerStaminaRollMax { get; set; } = 5.0;

    // On-hit raid GOLD = staminaCost × Uniform[GoldPerStaminaRollMin, GoldPerStaminaRollMax]
    // (mirrors the XP roll). Replaces the old flat RaidDefinition.GoldPerStamina multiplier.
    public double GoldPerStaminaRollMin { get; set; } = 3.0;
    public double GoldPerStaminaRollMax { get; set; } = 8.0;

    // ── T56 — health cost per hit ───────────────────────────────────────────────────────────────
    // Ordinary/guild raids pay a flat health cost by difficulty. The Gauntlet pays a Defense-scaled
    // mob-damage curve that ramps past GauntletHealthRampStage (~stage 200) to reflect late-game
    // difficulty. Health regenerates via the class rate; running out does NOT block a hit (PHASE-2:
    // optional 0-health gate). All values are appsettings-overridable for tuning.
    public Dictionary<string, int> RaidHealthCostByDifficulty { get; set; } = new()
    {
        ["Normal"] = 5, ["Hard"] = 10, ["Legendary"] = 20, ["Nightmare"] = 40,
    };
    public int RaidHealthCostDefault { get; set; } = 5;

    public double GauntletHealthBaseDamage { get; set; } = 8.0;
    public double GauntletHealthPerStage { get; set; } = 0.25;
    public int    GauntletHealthRampStage { get; set; } = 200;
    public double GauntletHealthRampPerStage { get; set; } = 2.0;
    // Defense reduces incoming Gauntlet damage by a FRACTION per point (capped), so high defense
    // softens but never zeroes the hit — damage stays noticeable at every stage.
    public double GauntletHealthDefenseReductionPerPoint { get; set; } = 0.001;
    public double GauntletHealthDefenseReductionMax { get; set; } = 0.8;

    // System 22 Phase A follow-up — Hoard scales raid threshold/kill CHANCE drops (item/magic/unit/
    // legion) exactly like ProcessQuestLootAsync scales quest chance drops. The boosted chance is
    // clamped at MaxThresholdDropChance, and the clamp never *lowers* a base that is already higher
    // (a base 1.0 "always" drop stays 1.0). Mirrors QuestConfig.MaxDropChance. Guaranteed (unconditional
    // gear) drops never pass through this scaling.
    public double MaxThresholdDropChance { get; set; } = 0.95;
}
