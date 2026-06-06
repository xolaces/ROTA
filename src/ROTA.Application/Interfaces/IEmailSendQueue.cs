namespace ROTA.Application.Interfaces;

/// <summary>
/// In-process hand-off between the request thread (which persists the row) and the background sender
/// (which performs the SMTP send). Keeps email delivery off the request path so a slow/failing provider
/// never blocks gameplay. Singleton.
/// </summary>
public interface IEmailSendQueue
{
    void Enqueue(Guid emailId);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}
