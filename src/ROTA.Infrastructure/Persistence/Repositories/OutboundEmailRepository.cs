using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class OutboundEmailRepository : IOutboundEmailRepository
{
    private readonly RotaDbContext _db;

    public OutboundEmailRepository(RotaDbContext db) => _db = db;

    public async Task AddAsync(OutboundEmail email, CancellationToken ct = default)
    {
        _db.OutboundEmails.Add(email);
        await _db.SaveChangesAsync(ct);
    }

    public Task<OutboundEmail?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.OutboundEmails.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct);

    public async Task UpdateAsync(OutboundEmail email, CancellationToken ct = default)
    {
        _db.OutboundEmails.Update(email);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<OutboundEmailPage> ListAsync(
        EmailType? type,
        EmailReviewStatus? reviewStatus,
        string? search,
        int page,
        int pageSize,
        EmailPriority? priority = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var q = _db.OutboundEmails.AsNoTracking().Where(e => !e.IsDeleted);

        if (type.HasValue) q = q.Where(e => e.Type == type.Value);
        if (reviewStatus.HasValue) q = q.Where(e => e.ReviewStatus == reviewStatus.Value);
        if (priority.HasValue) q = q.Where(e => e.Priority == priority.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => EF.Functions.ILike(e.Subject, $"%{s}%") || EF.Functions.ILike(e.Summary, $"%{s}%"));
        }

        // T52 — default sort is priority-first (High → Low), newest within each band; "created" sorts
        // purely by recency (the pre-T52 behaviour).
        var ordered = string.Equals(sort, "created", StringComparison.OrdinalIgnoreCase)
            ? q.OrderByDescending(e => e.CreatedAt)
            : q.OrderByDescending(e => e.Priority).ThenByDescending(e => e.CreatedAt);

        var total = await q.CountAsync(ct);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new OutboundEmailPage(items, total);
    }

    public async Task<OutboundEmailStats> GetStatsAsync(CancellationToken ct = default)
    {
        var q = _db.OutboundEmails.AsNoTracking().Where(e => !e.IsDeleted);

        var total = await q.CountAsync(ct);
        var pending = await q.CountAsync(e => e.ReviewStatus == EmailReviewStatus.Pending, ct);
        var approved = await q.CountAsync(e => e.ReviewStatus == EmailReviewStatus.Approved, ct);
        var dismissed = await q.CountAsync(e => e.ReviewStatus == EmailReviewStatus.Dismissed, ct);
        var sent = await q.CountAsync(e => e.SendStatus == EmailSendStatus.Sent, ct);
        var failed = await q.CountAsync(e => e.SendStatus == EmailSendStatus.Failed, ct);

        var byTypeRaw = await q
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byType = byTypeRaw.ToDictionary(x => x.Type.ToString(), x => x.Count);

        return new OutboundEmailStats(total, pending, approved, dismissed, sent, failed, byType);
    }

    // T71 ops hardening — rows still owed a send attempt (startup recovery sweep). Queued rows were
    // stranded by a restart (the channel is in-memory); Failed rows retry while attempts remain.
    public async Task<IReadOnlyList<Guid>> GetPendingSendIdsAsync(int maxAttempts, CancellationToken ct = default)
        => await _db.OutboundEmails.AsNoTracking()
            .Where(e => !e.IsDeleted
                        && (e.SendStatus == EmailSendStatus.Queued
                            || (e.SendStatus == EmailSendStatus.Failed && e.SendAttempts < maxAttempts)))
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Id)
            .ToListAsync(ct);
}
