namespace ROTA.Domain.Enums;

/// <summary>Delivery state of an <see cref="Entities.OutboundEmail"/>. The persisted row is the source
/// of truth; the actual SMTP send is best-effort and never blocks the triggering request.</summary>
public enum EmailSendStatus
{
    /// <summary>Persisted and enqueued; not yet attempted.</summary>
    Queued = 1,

    /// <summary>Delivered to the SMTP provider successfully.</summary>
    Sent = 2,

    /// <summary>Send attempted and failed; see <c>LastSendError</c>. Still visible in the dashboard.</summary>
    Failed = 3,
}
