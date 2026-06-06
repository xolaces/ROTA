namespace ROTA.Domain.Enums;

/// <summary>Operator triage state for an <see cref="Entities.OutboundEmail"/>. No automated action is
/// ever taken off the back of an email — the operator approves or dismisses it from the dashboard.</summary>
public enum EmailReviewStatus
{
    /// <summary>Awaiting operator review.</summary>
    Pending = 1,

    /// <summary>Operator acknowledged / actioned.</summary>
    Approved = 2,

    /// <summary>Operator dismissed (no action needed).</summary>
    Dismissed = 3,
}
