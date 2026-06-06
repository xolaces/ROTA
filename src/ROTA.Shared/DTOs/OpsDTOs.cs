namespace ROTA.Shared.DTOs;

/// <summary>A single outbound-email row for the ops dashboard. Enum fields are names (e.g. "PlayerReport").</summary>
public class OutboundEmailResponse
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    public Guid? TriggeringPlayerId { get; init; }
    public string? TriggeringSystem { get; init; }
    public string Summary { get; init; } = string.Empty;

    /// <summary>Raw JSON string (the row's jsonb detail) — the dashboard pretty-prints it.</summary>
    public string? Detail { get; init; }
    public string? Metadata { get; init; }

    public string SendStatus { get; init; } = string.Empty;
    public int SendAttempts { get; init; }
    public string? LastSendError { get; init; }

    public string ReviewStatus { get; init; } = string.Empty;
    public Guid? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Paged list response for GET /api/admin/emails.</summary>
public class OutboundEmailListResponse
{
    public IReadOnlyList<OutboundEmailResponse> Items { get; init; } = Array.Empty<OutboundEmailResponse>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>A pinnacle first-claim row for GET /api/admin/pinnacle-claims (T33).</summary>
public class PinnacleClaimResponse
{
    public int PinnacleLevel { get; init; }
    public Guid PlayerId { get; init; }
    public DateTimeOffset ClaimedAt { get; init; }
}

/// <summary>Aggregate counts for GET /api/admin/emails/stats (dashboard overview KPIs).</summary>
public class OpsEmailStatsResponse
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Approved { get; init; }
    public int Dismissed { get; init; }
    public int Sent { get; init; }
    public int Failed { get; init; }
    public IReadOnlyDictionary<string, int> ByType { get; init; } = new Dictionary<string, int>();
}
