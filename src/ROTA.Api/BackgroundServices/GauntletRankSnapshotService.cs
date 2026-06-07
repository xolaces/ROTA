using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;

namespace ROTA.Api.BackgroundServices;

/// <summary>
/// System 16 Slice 3 — periodically materialises the per-league rank snapshot
/// (<see cref="IGauntletScoringService.RecomputeRanksAsync"/>) for the single Active Gauntlet event,
/// every <c>GauntletConfig.ScoreSnapshotSeconds</c>. The leaderboard read serves these snapshotted
/// ranks (no per-request ranking).
/// <para>
/// This is a singleton hosted service but the repositories it drives are scoped, so it creates a fresh
/// DI scope per tick (mirrors <see cref="EmailSendBackgroundService"/>). It is resilient: a no-op when
/// no event is active, and a per-tick try/catch that logs and continues so a transient DB error never
/// crashes the host.
/// </para>
/// </summary>
public sealed class GauntletRankSnapshotService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GauntletRankSnapshotService> _log;
    private readonly TimeSpan _interval;

    public GauntletRankSnapshotService(
        IServiceScopeFactory scopes,
        IOptions<GauntletConfig> config,
        ILogger<GauntletRankSnapshotService> log)
    {
        _scopes = scopes;
        _log    = log;
        // Clamp to a sane floor so a misconfigured 0/negative value can't busy-spin.
        var seconds = Math.Max(1, config.Value.ScoreSnapshotSeconds);
        _interval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Gauntlet rank snapshot service started (interval {Seconds}s).", _interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);

        // Run once immediately, then on each tick, until shutdown.
        do
        {
            await SnapshotOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        _log.LogInformation("Gauntlet rank snapshot service stopping.");
    }

    private async Task SnapshotOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var events  = scope.ServiceProvider.GetRequiredService<IGauntletEventRepository>();
            var scoring = scope.ServiceProvider.GetRequiredService<IGauntletScoringService>();

            var active = await events.GetActiveAsync(ct);
            if (active is null)
                return; // no-op when nothing is running

            await scoring.RecomputeRanksAsync(active.Id, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — let the loop exit cleanly.
        }
        catch (Exception ex)
        {
            // Never let one bad tick kill the host; the next tick retries.
            _log.LogError(ex, "Gauntlet rank snapshot tick failed.");
        }
    }

    // Swallows the cancellation thrown by PeriodicTimer on shutdown so the loop ends without an
    // unhandled exception.
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
