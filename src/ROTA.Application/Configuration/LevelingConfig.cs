namespace ROTA.Application.Configuration;

public class LevelingConfig
{
    public double XpBaseMultiplier { get; set; } = 30.0;
    public double XpExponent { get; set; } = 0.7;

    public Dictionary<int, int> MilestoneFloors { get; set; } = new();

    public int GetFloor(int level)
        => MilestoneFloors
            .Where(kvp => level >= kvp.Key)
            .OrderByDescending(kvp => kvp.Key)
            .Select(kvp => (int?)kvp.Value)
            .FirstOrDefault() ?? 0;
}
