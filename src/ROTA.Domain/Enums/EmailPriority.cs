namespace ROTA.Domain.Enums;

/// <summary>
/// Operator-triage priority for an <c>outbound_emails</c> row (T52). Derived at submission time from the
/// email type: player reports are High, general feedback is Low, bug reports are Normal. The dashboard
/// sorts priority-first so the most urgent items surface at the top of the queue.
/// </summary>
public enum EmailPriority
{
    /// <summary>Low urgency — open-text player feedback (GeneralTicket).</summary>
    Low = 0,

    /// <summary>Default urgency — bug reports and any unclassified producer.</summary>
    Normal = 1,

    /// <summary>High urgency — player reports requiring moderator attention.</summary>
    High = 2,
}
