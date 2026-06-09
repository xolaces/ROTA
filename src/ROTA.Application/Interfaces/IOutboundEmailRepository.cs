using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

/// <summary>Persistence for the <c>outbound_emails</c> log — the ops dashboard's source of truth.</summary>
public interface IOutboundEmailRepository
{
    Task AddAsync(OutboundEmail email, CancellationToken ct = default);
    Task<OutboundEmail?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(OutboundEmail email, CancellationToken ct = default);

    Task<OutboundEmailPage> ListAsync(
        EmailType? type,
        EmailReviewStatus? reviewStatus,
        string? search,
        int page,
        int pageSize,
        EmailPriority? priority = null,
        string? sort = null,
        CancellationToken ct = default);

    Task<OutboundEmailStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>A page of outbound-email rows plus the unpaged total (for the dashboard table).</summary>
public sealed record OutboundEmailPage(IReadOnlyList<OutboundEmail> Items, int Total);

/// <summary>Aggregate counts for the dashboard overview.</summary>
public sealed record OutboundEmailStats(
    int Total,
    int Pending,
    int Approved,
    int Dismissed,
    int Sent,
    int Failed,
    IReadOnlyDictionary<string, int> ByType);
