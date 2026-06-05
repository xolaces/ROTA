namespace ROTA.Application.Configuration;

public class CombatConfig
{
    public double BaseCritChance { get; set; } = 0.05;
    public double MaxCritChanceBonus { get; set; } = 0.10;
    public double CritChancePerDiscernment { get; set; } = 0.0001;
    public double BaseCritMultiplier { get; set; } = 1.5;
    public double MaxCritDamageBonus { get; set; } = 1.0;
    public double CritDamagePerDiscernment { get; set; } = 0.0002;

    // On-hit raid XP = staminaCost × Uniform[XpPerStaminaRollMin, XpPerStaminaRollMax].
    // Defaults preserve the shipped curve (avg ~2.5 XP per stamina ⇒ ~50 on a 20-stamina hit).
    public double XpPerStaminaRollMin { get; set; } = 1.0;
    public double XpPerStaminaRollMax { get; set; } = 4.0;

    // On-hit raid GOLD = staminaCost × Uniform[GoldPerStaminaRollMin, GoldPerStaminaRollMax]
    // (mirrors the XP roll). Replaces the old flat RaidDefinition.GoldPerStamina multiplier.
    public double GoldPerStaminaRollMin { get; set; } = 3.0;
    public double GoldPerStaminaRollMax { get; set; } = 8.0;
}
