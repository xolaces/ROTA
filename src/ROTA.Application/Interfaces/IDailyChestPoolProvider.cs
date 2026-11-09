namespace ROTA.Application.Interfaces;

/// <summary>
/// System 29 — the weighted pool the daily chest rolls from (<c>content/daily_chest.json</c>). Validated
/// eagerly at startup: bad content fails the boot, not a player's pick.
/// </summary>
public interface IDailyChestPoolProvider
{
    IReadOnlyList<DailyChestPoolEntry> Pool { get; }

    /// <summary>Sum of all weights; always positive for a provider that constructed successfully.</summary>
    int TotalWeight { get; }
}

public sealed record DailyChestPoolEntry(string ItemDefinitionId, int Quantity, int Weight);
