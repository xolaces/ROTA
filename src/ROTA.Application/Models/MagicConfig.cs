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

    // BETA: The .NET IOptions binder appends appsettings values to this default list rather
    // than replacing it, so "World" cannot be removed via config — only additional tiers can
    // be added. To fully override DevOnlyTiers, replace it in ServiceCollectionExtensions
    // post-bind (operational note; no change required for current beta scope).
    public List<string> DevOnlyTiers          { get; set; } = new() { "World" };
    public double       MaxAggregateProcBonus { get; set; } = 5.0;

    public int GetSlotCount(string sizeStr)
        => SlotsBySize.TryGetValue(sizeStr, out var slots) ? slots : 1;
}
