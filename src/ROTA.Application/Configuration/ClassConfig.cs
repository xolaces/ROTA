using ROTA.Domain.Enums;

namespace ROTA.Application.Configuration;

public class ClassConfig
{
    public double GuildStaminaRegenMinutes { get; set; } = 2.0;
    public Dictionary<string, int> ClassUnlockLevels { get; set; } = new();
    public Dictionary<int, string> ConvergenceLevels { get; set; } = new();
    public Dictionary<string, Dictionary<string, double>> RegenMinutesPerPoint { get; set; } = new();

    public (double energy, double stamina) GetRegenRates(PlayerClass playerClass)
    {
        var name = playerClass.ToString()
            .Replace("Legendary", "")
            .Replace("Ascendant", "");
        if (RegenMinutesPerPoint.TryGetValue(name, out var rates))
            return (rates["Energy"], rates["Stamina"]);
        return (5.0, 5.0);
    }

    public string? GetConvergenceClass(int level)
        => ConvergenceLevels.TryGetValue(level, out var name) ? name : null;
}
