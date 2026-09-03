using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;

namespace ROTA.Api.BackgroundServices;

/// <summary>
/// Closes and settles Gauntlet events that have reached <c>EndsAt</c>.
/// <para>
/// A Gauntlet event opened on its own — <c>StartsAt</c> is gated live — but nothing ever closed it.
/// <c>EndsAt</c> was read only for display and to stamp each ladder stage's expiry, never compared
/// against now for a state transition. Until this existed, every prize-ranked player's tokens,
/// pitchfork, trophy and mastery credit waited on an admin remembering to press Close and then Settle;
/// and because a new event cannot open while an Active one exists, the stuck event halted the entire
/// Gauntlet system rather than just delaying one payout.
/// </para>
/// <para>
/// Modelled on <see cref="GauntletRankSnapshotService"/> — fresh DI scope per tick, per-tick try/catch
/// so a transient DB error never takes down the host, and one immediate run at startup so a restart
/// clears any backlog. It adds only the trigger: the payout itself is the same
/// <see cref="IGauntletAdminService.SettleEventAsync"/> an admin calls, so there is no parallel
/// implementation of the prize rules to drift out of step.
/// </para>
/// </summary>
public sealed class GauntletEventSettlementService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GauntletEventSettlementService> _log;
    private readonly TimeSpan _interval;

    public GauntletEventSettlementService(
        IServiceScopeFactory scopes,
        IOptions<GauntletConfig> config,
        ILogger<GauntletEventSettlementService> log)
    {
        _scopes = scopes;
        _log    = log;
        // Clamp to a sane floor so a misconfigured 0/negative value can't busy-spin.
        _interval = TimeSpan.FromSeconds(Math.Max(1, config.Value.SettlementSweepSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Gauntlet event settlement service started (interval {Seconds}s).", _interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);

        do
        {
            await SweepOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        _log.LogInformation("Gauntlet event settlement service stopping.");
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var admin = scope.ServiceProvider.GetRequiredService<IGauntletAdminService>();

            int settled = await admin.CloseAndSettleDueEventsAsync(ct);

            // Silent on the overwhelmingly common no-op tick. Settling an event pays real currency to
            // every ranked player, so the rare one is worth a line.
            if (settled > 0)
                _log.LogInformation("Closed and settled {Count} Gauntlet event(s).", settled);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — let the loop exit cleanly.
        }
        catch (Exception ex)
        {
            // Never let one bad tick kill the host; the next tick retries. Settlement is idempotent, so
            // a partially-completed sweep simply resumes.
            _log.LogError(ex, "Gauntlet event settlement tick failed.");
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
