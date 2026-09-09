using ROTA.Domain.Enums;

namespace ROTA.Application.Configuration;

public class ClassConfig
{
    public double GuildStaminaRegenMinutes { get; set; } = 2.0;
    // Health regen (minutes per point). Slow-but-steady late-game tension; class-tunable later.
    public double HealthRegenMinutes { get; set; } = 10.0;
    public Dictionary<string, int> ClassUnlockLevels { get; set; } = new();
    public Dictionary<int, string> ConvergenceLevels { get; set; } = new();
    public Dictionary<string, Dictionary<string, double>> RegenMinutesPerPoint { get; set; } = new();

    /// <summary>
    /// Speed granted by the AUTO tiers, applied on top of the chosen class's base rates. Legendary
    /// (level 500) and Ascendant (level 1,000) previously granted NOTHING -- the prefixes were
    /// stripped and the tier-3 rates returned verbatim -- so a player's regen never moved between
    /// level 100 and level 2,000. Two promotions, nineteen hundred levels, no change.
    ///
    /// A multiplier rather than 24 more config rows: the tier speeds a class up without disturbing
    /// its energy/stamina LEAN, which is what distinguishes the classes from each other.
    /// </summary>
    public Dictionary<string, double> TierSpeedMultiplier { get; set; } = new()
    {
        ["Legendary"] = 1.072,
        ["Ascendant"] = 1.142,
    };

    /// <summary>
    /// Regen interval quantum. Every rate is snapped to a whole number of these, so a countdown
    /// always lands on a five-second boundary -- 1:45, 2:10, never 1:47. Snapping here rather than in
    /// the config means it holds for tier-multiplied values too, which no hand-authored table could
    /// guarantee.
    /// </summary>
    public const double RegenQuantumMinutes = 5.0 / 60.0;

    private static double Snap(double minutes)
    {
        double q = Math.Round(minutes / RegenQuantumMinutes, MidpointRounding.AwayFromZero);
        return Math.Max(RegenQuantumMinutes, q * RegenQuantumMinutes);
    }

    public (double energy, double stamina) GetRegenRates(PlayerClass playerClass)
    {
        var full = playerClass.ToString();
        var name = full.Replace("Legendary", "").Replace("Ascendant", "");

        if (!RegenMinutesPerPoint.TryGetValue(name, out var rates))
            return (5.0, 5.0);

        double e = rates["Energy"], s = rates["Stamina"];

        // Auto-tier speed-up. Dividing the interval makes it FASTER; the lean is untouched because
        // both pools scale by the same factor.
        foreach (var (prefix, mult) in TierSpeedMultiplier)
            if (mult > 0 && full.StartsWith(prefix, StringComparison.Ordinal))
            {
                e /= mult; s /= mult;
                break;
            }

        return (Snap(e), Snap(s));
    }

    public string? GetConvergenceClass(int level)
        => ConvergenceLevels.TryGetValue(level, out var name) ? name : null;
}
