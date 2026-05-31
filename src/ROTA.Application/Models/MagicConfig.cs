namespace ROTA.Application.Models;

public class MagicConfig
{
    public Dictionary<string, int> SlotsBySize   { get; set; } = new()
    {
        ["Personal"] = 1,
        ["Small"]    = 2,
        ["Medium"]   = 3,
        ["Large"]    = 4,
        ["Titanic"]  = 5,
    };

    public List<string> DevOnlyTiers          { get; set; } = new() { "World" };
    public double       MaxAggregateProcBonus { get; set; } = 5.0;

    public int GetSlotCount(string sizeStr)
        => SlotsBySize.TryGetValue(sizeStr, out var slots) ? slots : 1;
}
