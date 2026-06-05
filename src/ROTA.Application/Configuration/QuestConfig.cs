namespace ROTA.Application.Configuration;

// Quest node depletion (System 20). Each node starts at NodeStartProgress and depletes per
// attempt until it hits 0 and clears, unlocking the next node. Boss nodes deplete slower to
// reflect their weight. Config-driven (appsettings "QuestConfig") — no magic numbers in the service.
public class QuestConfig
{
    public double NodeStartProgress { get; set; } = 100.0;
    public double BattleDepletionPerAttempt { get; set; } = 5.0;   // 100 / 5  = 20 attempts to clear
    public double BossDepletionPerAttempt { get; set; } = 2.5;     // 100 / 2.5 = 40 attempts to clear
}
