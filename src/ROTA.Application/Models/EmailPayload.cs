using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

/// <summary>
/// The shape every producer (T33/T37/T38/T40) builds and hands to <c>IEmailNotificationService</c>.
/// One payload type keeps all operator notifications consistent; adding a new <see cref="EmailType"/>
/// is a one-line enum addition plus a producer that fills this in.
/// </summary>
public sealed class EmailPayload
{
    public required EmailType Type { get; init; }

    /// <summary>Short subject (the service prefixes <c>[ROTA][Type]</c>).</summary>
    public required string Subject { get; init; }

    /// <summary>One-line human summary for the dashboard list.</summary>
    public required string Summary { get; init; }

    /// <summary>Reporter / submitter / target / claimant — nullable for system events.</summary>
    public Guid? TriggeringPlayerId { get; init; }

    /// <summary>Originating ticket/system, e.g. "T40" or "BugSubmission".</summary>
    public string? TriggeringSystem { get; init; }

    /// <summary>Structured detail, serialized to the row's <c>detail</c> jsonb column.</summary>
    public IReadOnlyDictionary<string, object?>? Detail { get; init; }

    /// <summary>Optional metadata, serialized to the <c>metadata</c> jsonb column.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}
