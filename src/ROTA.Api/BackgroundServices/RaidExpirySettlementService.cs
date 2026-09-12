using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;

namespace ROTA.Api.BackgroundServices;

/// <summary>
/// Settles World (timer-only) raids whose clock has run out, and removes raids with nothing left to do.
/// <para>
/// A World raid carries <c>MaxHp 0</c> and is never killed by damage, so the kill branch in
/// <see cref="IRaidService.HitRaidAsync"/> never fires for it. Expiry is its ONLY ending. Without this
/// service nothing ran at that moment: the raid stayed <c>Active</c> forever and every participant's
/// banked ladder damage went unpaid.
/// </para>
/// <para>
/// Modelled on <see cref="GauntletRankSnapshotService"/>: a singleton hosted service driving scoped
/// repositories, so it opens a fresh DI scope per tick, and a per-tick try/catch keeps a transient DB
/// error from taking down the host. It differs in two ways — it runs on a fixed interval from
/// <see cref="RaidConfig"/> rather than a Gauntlet-specific cadence, and the work it drives is
/// idempotent under an advisory lock, so a double-fire, a restart mid-settlement, or a second app
/// instance on the same database all settle each raid exactly once.
/// </para>
/// </summary>
public sealed class RaidExpirySettlementService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RaidExpirySettlementService> _log;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;

    public RaidExpirySettlementService(
        IServiceScopeFactory scopes,
        IOptions<RaidConfig> config,
        ILogger<RaidExpirySettlementService> log)
    {
        _scopes = scopes;
        _log    = log;
        // Clamp both so a misconfigured 0/negative value can't busy-spin or select an empty batch.
        _interval  = TimeSpan.FromSeconds(Math.Max(1, config.Value.ExpirySweepSeconds));
        _batchSize = Math.Max(1, config.Value.ExpirySweepBatchSize);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Raid expiry settlement service started (interval {Seconds}s, batch {Batch}).",
            _interval.TotalSeconds, _batchSize);

        using var timer = new PeriodicTimer(_interval);

        // Run once immediately so a restart clears any backlog that accrued while the host was down,
        // then on each tick until shutdown.
        do
        {
            await SweepOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        _log.LogInformation("Raid expiry settlement service stopping.");
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var raids = scope.ServiceProvider.GetRequiredService<IRaidService>();

            int settled = await raids.SettleExpiredRaidsAsync(_batchSize, ct);
            int removed = await raids.PurgeSpentRaidsAsync(_batchSize, ct);

            // Silent on the overwhelmingly common no-op tick; a settlement is rare and worth a line.
            if (settled > 0)
                _log.LogInformation("Settled {Count} expired World raid(s).", settled);
            if (removed > 0)
                _log.LogInformation("Removed {Count} spent raid(s).", removed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — let the loop exit cleanly.
        }
        catch (Exception ex)
        {
            // Never let one bad tick kill the host; the next tick retries. Settlement is latched, so a
            // partially-completed sweep simply resumes.
            _log.LogError(ex, "Raid expiry settlement tick failed.");
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
