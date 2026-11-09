using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;

namespace ROTA.Application.Services;

/// <summary>System 29 S1 — see <see cref="ICheckInService"/>.</summary>
public sealed class CheckInService : ICheckInService
{
    private readonly IPlayerRepository _players;
    private readonly IAchievementService _achievements;

    public CheckInService(IPlayerRepository players, IAchievementService achievements)
    {
        _players = players;
        _achievements = achievements;
    }

    public async Task<CheckInResult> CheckInAsync(Guid playerId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // MutateWithRetryAsync, not UpdateAsync. This now runs on every token refresh, alongside live
        // gameplay. UpdateAsync marks the whole players row modified, so a raid hit committing between
        // our read and our write trips the xmin token, and the failed change would stay pending in the
        // request's DbContext, breaking the refresh-token save that follows it. The retry loop reloads
        // the committed row and re-applies, so a lost race resolves instead of leaking.
        var result = await _players.MutateWithRetryAsync(playerId, p =>
        {
            var previous = p.LastLoginDate;
            return new CheckInResult(p.RecordLogin(today), previous);
        }, ct);

        if (result.NewDay)
        {
            // Per-day referenceId: even if two check-ins both see a new day, the metric counts once.
            await _achievements.RecordProgressAsync(
                playerId, AchievementMetric.DaysPlayed, 1, $"ach:day:{playerId}:{today:yyyy-MM-dd}", ct);
            await _achievements.EvaluateCompletionsAsync(playerId, ct);
        }

        return result;
    }
}
