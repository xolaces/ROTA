namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// An operator notification persisted to <c>outbound_emails</c> — the dashboard's source of truth.
/// Created by <c>EmailNotificationService</c> (persist-first); the SMTP send is attempted afterwards
/// on a background queue and updates <see cref="SendStatus"/>. Never mutated except via these methods.
/// </summary>
public class OutboundEmail
{
    // Required by EF Core
    private OutboundEmail() { }

    public static OutboundEmail Create(
        EmailType type,
        string subject,
        string recipient,
        string summary,
        Guid? triggeringPlayerId,
        string? triggeringSystem,
        string? detailJson,
        string? metadataJson)
        => new OutboundEmail
        {
            Id = Guid.NewGuid(),
            Type = type,
            Subject = subject,
            Recipient = recipient,
            Summary = summary,
            TriggeringPlayerId = triggeringPlayerId,
            TriggeringSystem = triggeringSystem,
            DetailJson = detailJson,
            MetadataJson = metadataJson,
            SendStatus = EmailSendStatus.Queued,
            SendAttempts = 0,
            ReviewStatus = EmailReviewStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false,
        };

    public Guid Id { get; private set; }

    /// <summary>Classification (subject tag + dashboard grouping).</summary>
    public EmailType Type { get; private set; }

    /// <summary>Full subject line, already tagged: <c>[ROTA][PlayerReport] …</c>.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>Operator inbox (default rotadevteam@gmail.com).</summary>
    public string Recipient { get; private set; } = string.Empty;

    /// <summary>Player who triggered this (reporter, submitter, target, claimant) — nullable for system events.</summary>
    public Guid? TriggeringPlayerId { get; private set; }

    /// <summary>Originating system/ticket (e.g. "T40", "BugSubmission") — free text, nullable.</summary>
    public string? TriggeringSystem { get; private set; }

    /// <summary>One-line human summary shown in the dashboard list.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>Structured payload (jsonb) — the producer's detail object.</summary>
    public string? DetailJson { get; private set; }

    /// <summary>Free-form metadata (jsonb).</summary>
    public string? MetadataJson { get; private set; }

    public EmailSendStatus SendStatus { get; private set; }
    public int SendAttempts { get; private set; }
    public string? LastSendError { get; private set; }

    public EmailReviewStatus ReviewStatus { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; }

    /// <summary>Marks the row delivered. Bumps attempt count and clears any prior error.</summary>
    public void MarkSent()
    {
        SendStatus = EmailSendStatus.Sent;
        SendAttempts += 1;
        LastSendError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records a failed send attempt (truncates the error to 1000 chars). Non-fatal.</summary>
    public void MarkFailed(string error)
    {
        SendStatus = EmailSendStatus.Failed;
        SendAttempts += 1;
        LastSendError = string.IsNullOrEmpty(error) ? "(unknown error)"
            : error.Length > 1000 ? error[..1000] : error;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Operator triage: acknowledged / actioned.</summary>
    public void Approve(Guid reviewerId)
    {
        ReviewStatus = EmailReviewStatus.Approved;
        ReviewedBy = reviewerId;
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Operator triage: dismissed (no action).</summary>
    public void Dismiss(Guid reviewerId)
    {
        ReviewStatus = EmailReviewStatus.Dismissed;
        ReviewedBy = reviewerId;
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
