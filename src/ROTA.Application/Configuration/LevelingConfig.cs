namespace ROTA.Application.Configuration;

public class LevelingConfig
{
    public double XpBaseMultiplier { get; set; } = 30.0;
    public double XpExponent { get; set; } = 0.7;

    public Dictionary<int, int> MilestoneFloors { get; set; } = new();

    /// <summary>
    /// Gem rewards granted on reaching exact pinnacle / milestone levels (T32): level → gems.
    /// Class-gate levels (the mandatory class-select overlay) are <c>ClassConfig.ConvergenceLevels</c>;
    /// this map is the gem grant, which also includes two gem-only milestones (1000, 2500) that are NOT
    /// class gates. The convergence tiers 2000/15000/25000 are intentionally absent until their gem
    /// amounts are confirmed — a level not present here grants no pinnacle gems.
    /// </summary>
    public Dictionary<int, int> PinnacleGemRewards { get; set; } = new();

    public int GetFloor(int level)
        => MilestoneFloors
            .Where(kvp => level >= kvp.Key)
            .OrderByDescending(kvp => kvp.Key)
            .Select(kvp => (int?)kvp.Value)
            .FirstOrDefault() ?? 0;

    /// <summary>Gems to grant on reaching exactly <paramref name="level"/> (0 when it is not a pinnacle level).</summary>
    public int GetPinnacleGems(int level)
        => PinnacleGemRewards.TryGetValue(level, out var gems) ? gems : 0;

    /// <summary>
    /// True if <paramref name="level"/> is a configured pinnacle level. Drives both the gem reward (T32)
    /// and the first-claim logging (T33), so a single config map is the one source of "pinnacle levels".
    /// </summary>
    public bool IsPinnacleLevel(int level) => PinnacleGemRewards.ContainsKey(level);
}
