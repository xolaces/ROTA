using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;

namespace ROTA.Api.BackgroundServices;

/// <summary>
/// Drains <see cref="IEmailSendQueue"/> and performs the actual SMTP send out of band, so a slow or
/// failing provider never blocks a gameplay request. Each item is processed in its own DI scope.
/// T71 ops hardening: on startup, re-enqueues rows stranded Queued by a restart (the channel is
/// in-memory) plus Failed rows with attempts remaining; a retryable failure is re-enqueued after
/// a delay until <see cref="EmailConfig.MaxSendAttempts"/> is exhausted.
/// </summary>
public sealed class EmailSendBackgroundService : BackgroundService
{
    private readonly IEmailSendQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly EmailConfig _cfg;
    private readonly ILogger<EmailSendBackgroundService> _log;

    public EmailSendBackgroundService(
        IEmailSendQueue queue,
        IServiceScopeFactory scopes,
        IOptions<EmailConfig> cfg,
        ILogger<EmailSendBackgroundService> log)
    {
        _queue = queue;
        _scopes = scopes;
        _cfg = cfg.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Outbound email sender started.");

        await RecoverStrandedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid id;
            try
            {
                id = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _scopes.CreateScope();
                var notifier = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
                bool settled = await notifier.ProcessSendAsync(id, stoppingToken);
                if (!settled)
                    ScheduleRetry(id, stoppingToken);
            }
            catch (Exception ex)
            {
                // Never let one bad item kill the loop.
                _log.LogError(ex, "Unhandled error processing outbound email {Id}", id);
            }
        }

        _log.LogInformation("Outbound email sender stopping.");
    }

    // T71 — startup sweep: anything persisted but not Sent gets back into the channel. Failures here
    // must never block startup; the rows stay visible in the dashboard either way.
    private async Task RecoverStrandedAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var emails = scope.ServiceProvider.GetRequiredService<IOutboundEmailRepository>();
            var pending = await emails.GetPendingSendIdsAsync(_cfg.MaxSendAttempts, ct);
            foreach (var id in pending)
                _queue.Enqueue(id);
            if (pending.Count > 0)
                _log.LogInformation("Recovered {Count} stranded outbound email(s) into the send queue.", pending.Count);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "Outbound email startup recovery sweep failed (rows remain in the dashboard).");
        }
    }

    // T71 — fire-and-forget delayed re-enqueue. The row's SendAttempts (not this task) bounds the
    // total tries, so a lost retry task (process exit) is healed by the next startup sweep.
    private void ScheduleRetry(Guid id, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _cfg.RetrySendDelaySeconds)), ct);
                _queue.Enqueue(id);
            }
            catch (OperationCanceledException) { /* shutting down — startup sweep picks it up */ }
        }, CancellationToken.None);
    }
}
