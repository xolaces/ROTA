using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

/// <summary>
/// The single entry point every producer uses to raise an operator notification. <see cref="QueueAsync"/>
/// persists the row (source of truth) + audit entry and enqueues the send — it never blocks on SMTP and
/// never throws on a delivery problem. <see cref="ProcessSendAsync"/> is driven by the background sender.
/// </summary>
public interface IEmailNotificationService
{
    /// <summary>Persist + audit + enqueue. Returns the new outbound-email id. Non-blocking, non-throwing on send.</summary>
    Task<Guid> QueueAsync(EmailPayload payload, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>
    /// Loads the row, attempts the SMTP send, records sent/failed. Swallows send failures.
    /// Returns true when the row needs no further attempt (sent, missing, or attempts exhausted);
    /// false when the failure is retryable (T71 — the background sender re-enqueues after a delay).
    /// </summary>
    Task<bool> ProcessSendAsync(Guid emailId, CancellationToken ct = default);
}
