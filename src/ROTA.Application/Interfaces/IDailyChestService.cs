using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>System 29 — the free daily chest: six closed chests, pick one, all six revealed.</summary>
public interface IDailyChestService
{
    /// <summary>Today's chest, rolling it first if this is the day's first read.</summary>
    Task<DailyChestResponse> GetTodayAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Picks chest <paramref name="index"/> (0–5), grants it, and reveals all six. Exactly once per day.</summary>
    Task<PickDailyChestResponse> PickAsync(Guid playerId, int index, CancellationToken ct = default);
}
