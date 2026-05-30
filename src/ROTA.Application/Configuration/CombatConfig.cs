namespace ROTA.Application.Configuration;

public class CombatConfig
{
    public double BaseCritChance { get; set; } = 0.05;
    public double MaxCritChanceBonus { get; set; } = 0.10;
    public double CritChancePerDiscernment { get; set; } = 0.0001;
    public double BaseCritMultiplier { get; set; } = 1.5;
    public double MaxCritDamageBonus { get; set; } = 1.0;
    public double CritDamagePerDiscernment { get; set; } = 0.0002;
}
