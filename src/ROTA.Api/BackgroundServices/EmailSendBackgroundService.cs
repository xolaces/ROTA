using ROTA.Application.Interfaces;

namespace ROTA.Api.BackgroundServices;

/// <summary>
/// Drains <see cref="IEmailSendQueue"/> and performs the actual SMTP send out of band, so a slow or
/// failing provider never blocks a gameplay request. Each item is processed in its own DI scope.
/// </summary>
public sealed class EmailSendBackgroundService : BackgroundService
{
    private readonly IEmailSendQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailSendBackgroundService> _log;

    public EmailSendBackgroundService(
        IEmailSendQueue queue,
        IServiceScopeFactory scopes,
        ILogger<EmailSendBackgroundService> log)
    {
        _queue = queue;
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Outbound email sender started.");

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
                await notifier.ProcessSendAsync(id, stoppingToken);
            }
            catch (Exception ex)
            {
                // Never let one bad item kill the loop.
                _log.LogError(ex, "Unhandled error processing outbound email {Id}", id);
            }
        }

        _log.LogInformation("Outbound email sender stopping.");
    }
}
