using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// TICKET 46 — Achievement Point system. Tracks per-player metric counters at the existing
/// combat/loot/login chokepoints, awards points once per achievement via an append-only ledger
/// (gem-ledger idempotency discipline), and surfaces the overview + total AP. Mirrors
/// <see cref="IMasteryService"/>; all recording is best-effort at the chokepoints (never blocks gameplay).
/// </summary>
public interface IAchievementService
{
    /// <summary>
    /// Records cumulative metric activity (delta metrics: raids/quests/days). When
    /// <paramref name="referenceId"/> is supplied the increment is exactly-once (idempotency via the
    /// award ledger pre-check). Does NOT evaluate completions — call <see cref="EvaluateCompletionsAsync"/>.
    /// </summary>
    Task RecordProgressAsync(Guid playerId, AchievementMetric metric, long amount = 1,
        string? referenceId = null, CancellationToken ct = default);

    /// <summary>
    /// Sets the absolute counter for EVERY achievement on the given metric (recount metrics —
    /// EquipmentPiecesOwned). Use when a single absolute value applies to all achievements on the metric.
    /// For Collector (per-key counts) use <see cref="RecountCollectorCountersAsync"/>.
    /// </summary>
    Task SetCounterAsync(Guid playerId, AchievementMetric metric, long absoluteValue, CancellationToken ct = default);

    /// <summary>
    /// Recounts each Collector achievement's distinct-owned-item count by ITS OWN CollectorKey (matched
    /// against item Type name or Tags) and writes the absolute per-achievement counter. So a re-grant of
    /// an already-owned item never inflates, and distinct keys never clobber each other.
    /// </summary>
    Task RecountCollectorCountersAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Awards every achievement whose counter ≥ threshold and is not yet awarded — exactly one ledger
    /// row each (unique index), latching IsCompleted + auditing "AchievementUnlocked". Idempotent;
    /// safe to call repeatedly. Called on profile read and after quest completion.
    /// </summary>
    Task EvaluateCompletionsAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Full overview for GET /api/achievements (evaluates completions, then total AP + per-achievement state).</summary>
    Task<AchievementOverviewResponse> GetForPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Total Achievement Points = SUM over the award ledger. Used by the profile hydration.</summary>
    Task<int> GetTotalPointsAsync(Guid playerId, CancellationToken ct = default);
}
