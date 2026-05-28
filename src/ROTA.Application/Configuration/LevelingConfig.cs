namespace ROTA.Application.Configuration;

/// <summary>
/// Configures the XP-per-level formula and milestone floors.
/// Bound from appsettings.json "LevelingConfig" section.
/// </summary>
// BETA — formula: baseXp = XpBaseMultiplier × level^XpExponent; result = max(floor, round(baseXp))
public class LevelingConfig
{
    public double XpBaseMultiplier { get; set; } = 30.0;
    public double XpExponent { get; set; } = 0.7;

    /// <summary>
    /// Keys are level thresholds; values are minimum XP required to level from that level.
    /// The highest matching key wins. Ensures progression doesn't stall at milestone levels.
    /// </summary>
    public Dictionary<int, int> MilestoneFloors { get; set; } = new();

    /// <summary>
    /// Returns the highest milestone floor that applies at or below the given level.
    /// Returns 0 if no milestone floor applies.
    /// </summary>
    public int GetFloor(int level)
        => MilestoneFloors
            .Where(kvp => level >= kvp.Key)
            .OrderByDescending(kvp => kvp.Key)
            .Select(kvp => (int?)kvp.Value)
            .FirstOrDefault() ?? 0;
}
