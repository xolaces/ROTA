using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;

namespace ROTA.Api.BackgroundServices;

/// <summary>
/// Copies every eligible player's ATK / DEF / Discernment into the three Live stat boards
/// (<see cref="ILeaderboardService.SnapshotStatBoardAsync"/>) at startup and then every
/// <c>LeaderboardConfig.StatSnapshotSeconds</c>.
/// <para>
/// Before this existed (2026-11-10) the snapshot only ran when an admin triggered it by hand, so on
/// production the stat boards were empty. The quest and raid boards fill as players act and never
/// needed this.
/// </para>
/// <para>
/// Same shape as <see cref="GauntletRankSnapshotService"/>: a fresh DI scope per tick because the
/// repositories are scoped, and a per-tick try/catch so a transient DB error never stops the host.
/// A no-op while <c>StatBoardEnabled</c> is false.
/// </para>
/// </summary>
public sealed class LeaderboardStatSnapshotService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<LeaderboardConfig> _config;
    private readonly ILogger<LeaderboardStatSnapshotService> _log;
    private readonly TimeSpan _interval;

    public LeaderboardStatSnapshotService(
        IServiceScopeFactory scopes,
        IOptions<LeaderboardConfig> config,
        ILogger<LeaderboardStatSnapshotService> log)
    {
        _scopes = scopes;
        _config = config;
        _log    = log;
        // Clamp to a sane floor so a misconfigured 0/negative value can't busy-spin.
        _interval = TimeSpan.FromSeconds(Math.Max(30, config.Value.StatSnapshotSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Leaderboard stat snapshot service started (interval {Seconds}s).", _interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);

        do
        {
            await SnapshotOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        _log.LogInformation("Leaderboard stat snapshot service stopping.");
    }

    private async Task SnapshotOnceAsync(CancellationToken ct)
    {
        if (!_config.Value.StatBoardEnabled)
            return;

        try
        {
            using var scope = _scopes.CreateScope();
            var leaderboards = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();
            var count = await leaderboards.SnapshotStatBoardAsync(ct);
            _log.LogDebug("Leaderboard stat snapshot wrote {Count} players.", count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — let the loop exit cleanly.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Leaderboard stat snapshot tick failed.");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
