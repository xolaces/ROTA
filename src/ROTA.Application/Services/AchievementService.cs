using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// TICKET 46 — Achievement Point service. Records per-player metric counters at the existing
/// chokepoints, awards points once per achievement via the append-only ledger (gem-ledger
/// idempotency discipline), and surfaces the overview + total AP. Mirrors <c>MasteryService</c>.
/// </summary>
public sealed class AchievementService : IAchievementService
{
    private readonly IAchievementDefinitionProvider _defs;
    private readonly IAchievementProgressRepository _progress;
    private readonly IAchievementProgressEventRepository _progressEvents;
    private readonly IAchievementAwardRepository _awards;
    private readonly IAuditLogRepository _auditLog;
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IItemDefinitionProvider _itemDefs;

    public AchievementService(
        IAchievementDefinitionProvider defs,
        IAchievementProgressRepository progress,
        IAchievementProgressEventRepository progressEvents,
        IAchievementAwardRepository awards,
        IAuditLogRepository auditLog,
        IPlayerInventoryRepository inventory,
        IItemDefinitionProvider itemDefs)
    {
        _defs           = defs;
        _progress       = progress;
        _progressEvents = progressEvents;
        _awards         = awards;
        _auditLog       = auditLog;
        _inventory      = inventory;
        _itemDefs       = itemDefs;
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    public async Task RecordProgressAsync(Guid playerId, AchievementMetric metric, long amount = 1,
        string? referenceId = null, CancellationToken ct = default)
    {
        if (amount <= 0) return;

        // Each achievement on this metric is its own progress row (so chained tiers track in lockstep).
        var defs = _defs.GetByMetric(metric);
        if (defs.Count == 0) return;

        foreach (var def in defs)
        {
            if (referenceId is not null)
            {
                // Exactly-once for referenced seams (e.g. a per-day login, a per-(raid,player) kill):
                // record the progress event FIRST against the dedicated append-only event ledger,
                // scoped per (player, achievement, source ref) so the same ref is independent across
                // tiers on the metric. CreateAsync returns false on a unique-violation (concurrent
                // dup) — in either replay case (already-exists or lost the race) SKIP the increment.
                var created = await _progressEvents.CreateAsync(
                    AchievementProgressEvent.Create(playerId, def.Id, referenceId), ct);
                if (!created) continue;
            }
            await _progress.IncrementAsync(playerId, def.Id, amount, ct);
        }
    }

    public async Task SetCounterAsync(Guid playerId, AchievementMetric metric, long absoluteValue, CancellationToken ct = default)
    {
        if (absoluteValue < 0) return;
        foreach (var def in _defs.GetByMetric(metric))
            await _progress.SetCounterAsync(playerId, def.Id, absoluteValue, ct);
    }

    public async Task RecountCollectorCountersAsync(Guid playerId, CancellationToken ct = default)
    {
        var collectorDefs = _defs.GetByMetric(AchievementMetric.CollectorItemCount)
            .Where(d => !string.IsNullOrWhiteSpace(d.CollectorKey))
            .ToList();
        if (collectorDefs.Count == 0) return;

        // Distinct owned items (quantity > 0), hydrated with their definition for key matching.
        var ownedDefs = (await _inventory.GetAllForPlayerAsync(playerId, ct))
            .Where(i => i.Quantity > 0)
            .Select(i => _itemDefs.GetById(i.ItemDefinitionId))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        // Each Collector achievement counts by ITS OWN key (item Type name or a Tag) — distinct keys
        // never clobber each other.
        foreach (var def in collectorDefs)
        {
            var key = def.CollectorKey!;
            long count = ownedDefs.Count(d =>
                string.Equals(d.Type.ToString(), key, StringComparison.OrdinalIgnoreCase)
                || d.Tags.Any(t => string.Equals(t, key, StringComparison.OrdinalIgnoreCase)));
            await _progress.SetCounterAsync(playerId, def.Id, count, ct);
        }
    }

    // ── Completion evaluation (idempotent) ────────────────────────────────────

    public async Task EvaluateCompletionsAsync(Guid playerId, CancellationToken ct = default)
    {
        var progressById = (await _progress.GetForPlayerAsync(playerId, ct))
            .ToDictionary(p => p.AchievementId, p => p);

        foreach (var def in _defs.GetAll())
        {
            if (!progressById.TryGetValue(def.Id, out var row)) continue;
            if (row.ProgressValue < def.Threshold) continue;
            if (row.IsCompleted) continue;

            await AwardAsync(playerId, def, row, ct);
        }
    }

    private async Task AwardAsync(Guid playerId, Models.AchievementDefinition def, AchievementProgress row, CancellationToken ct)
    {
        // One award per achievement — the unique index is the hard idempotency backstop; CreateAsync
        // returns false on a concurrent dup. The reference encodes the (player, achievement) scope.
        var referenceId = $"achievement:{playerId}:{def.Id}";
        if (await _awards.ReferenceExistsAsync(playerId, referenceId, ct))
        {
            // Already awarded (e.g. a crash after the ledger write but before the latch) — latch now.
            await LatchCompleteAsync(row, ct);
            return;
        }

        var created = await _awards.CreateAsync(
            AchievementAward.Create(playerId, def.Id, def.Points, referenceId), ct);

        // Latch the progress row's completion flag regardless of who won the create race.
        await LatchCompleteAsync(row, ct);

        if (created)
            await _auditLog.AppendAsync(AuditLog.Create(
                playerId, "AchievementUnlocked", null,
                $"{def.Id} (+{def.Points} AP)", null), ct);
    }

    private async Task LatchCompleteAsync(AchievementProgress row, CancellationToken ct)
    {
        if (row.IsCompleted) return;
        row.MarkComplete(DateTimeOffset.UtcNow);
        await _progress.UpsertAsync(row, ct);
    }

    // ── Reads ─────────────────────────────────────────────────────────────────

    public async Task<AchievementOverviewResponse> GetForPlayerAsync(Guid playerId, CancellationToken ct = default)
    {
        // Read is one of the two trigger points — evaluate any earned-but-unawarded achievements first.
        await EvaluateCompletionsAsync(playerId, ct);

        var progressById = (await _progress.GetForPlayerAsync(playerId, ct))
            .ToDictionary(p => p.AchievementId, p => p);
        int total = await _awards.GetTotalPointsAsync(playerId, ct);

        var entries = new List<AchievementEntryDto>();
        foreach (var def in _defs.GetAll())
        {
            progressById.TryGetValue(def.Id, out var row);
            long current = row?.ProgressValue ?? 0;
            entries.Add(new AchievementEntryDto
            {
                Id          = def.Id,
                Category    = def.Category.ToString(),
                Metric      = def.Metric.ToString(),
                Name        = def.Name,
                Description = def.Description,
                Points      = def.Points,
                Threshold   = def.Threshold,
                Progress    = Math.Min(current, def.Threshold),
                IsCompleted = row?.IsCompleted ?? false,
                CompletedAt = row?.CompletedAt,
                IconKey     = def.IconKey,
            });
        }

        return new AchievementOverviewResponse { TotalPoints = total, Achievements = entries };
    }

    public Task<int> GetTotalPointsAsync(Guid playerId, CancellationToken ct = default)
        => _awards.GetTotalPointsAsync(playerId, ct);
}
