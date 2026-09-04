using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;

namespace ROTA.Api.BackgroundServices;

/// <summary>
/// Returns the goods on market listings whose clock has run out.
/// <para>
/// This exists because of a defect shape this codebase has already been bitten by twice — a World
/// raid's <c>ExpiresAt</c> and a Gauntlet event's <c>EndsAt</c> were both REJECTION gates with no
/// SETTLEMENT behind them, so the clock ran out and nothing ran. A market listing is worse than
/// either, because the listing is HOLDING something: without this service an expired listing is not
/// merely stale, it has permanently eaten the seller's stack.
/// </para>
/// <para>
/// Modelled on <see cref="RaidExpirySettlementService"/>: a fresh DI scope per tick, a per-tick
/// try/catch so one bad tick cannot take the host down, and idempotent work underneath — each listing
/// settles under a status latch, so a double-fire, a restart mid-sweep, or a second app instance on
/// the same database all return each stack exactly once.
/// </para>
/// </summary>
public sealed class MarketExpirySettlementService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MarketExpirySettlementService> _log;
    private readonly bool _enabled;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 100;

    public MarketExpirySettlementService(
        IServiceScopeFactory scopes,
        IOptions<MarketConfig> config,
        ILogger<MarketExpirySettlementService> log)
    {
        _scopes  = scopes;
        _log     = log;
        // Read ONCE at construction: a market switched off after listings exist would otherwise
        // strand them, so the sweep is gated on the config only to avoid pointless work on a server
        // that has never had a market at all.
        _enabled = config.Value.Enabled;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _log.LogInformation("Market expiry sweep not started — the market is disabled.");
            return;
        }

        _log.LogInformation(
            "Market expiry sweep started (every {Minutes} min, batch {Batch}).",
            Interval.TotalMinutes, BatchSize);

        using var timer = new PeriodicTimer(Interval);

        // Once immediately, so a restart clears whatever lapsed while the host was down.
        do
        {
            await SweepOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        _log.LogInformation("Market expiry sweep stopping.");
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var market = scope.ServiceProvider.GetRequiredService<IMarketService>();

            int settled = await market.SettleExpiredListingsAsync(BatchSize, ct);
            if (settled > 0)
                _log.LogInformation("Returned the goods on {Count} lapsed listing(s).", settled);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — let the loop exit cleanly.
        }
        catch (Exception ex)
        {
            // Never let one bad tick kill the host. Settlement is latched, so a partial sweep resumes.
            _log.LogError(ex, "Market expiry sweep tick failed.");
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
