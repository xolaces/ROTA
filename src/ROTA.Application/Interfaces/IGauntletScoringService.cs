using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 3) — scoring aggregation + per-league snapshot rank + leaderboard read.
// Read-only over the participant damage already written by combat: the GauntletEntry.Score field is
// a denormalised running total updated per qualifying hit (the write is wired by Slice 4). No combat
// math lives here.
public interface IGauntletScoringService
{
    /// <summary>
    /// Atomically increments the (event, player) entry's score by <paramref name="deltaScore"/>.
    /// <c>TieBreakAt</c> advances to <paramref name="hitAt"/> ONLY when <paramref name="deltaScore"/>
    /// &gt; 0 (a zero/negative delta must not move the tiebreak). Race-safe single UPDATE — not
    /// load-modify-save. Called from the combat hit path in Slice 4; no-op if the entry is absent.
    /// </summary>
    Task UpdateScoreAsync(
        Guid playerId, Guid eventId, long deltaScore, DateTimeOffset hitAt, CancellationToken ct = default);

    /// <summary>
    /// Snapshots per-league ranks for the event into <c>LastRank</c>
    /// (<c>ROW_NUMBER() … ORDER BY score DESC, tie_break_at ASC</c>). Idempotent. Driven by the
    /// ~60s hosted service and at settlement (Slice 5).
    /// </summary>
    Task RecomputeRanksAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// The snapshot-ranked leaderboard for a single league in an event: the top
    /// <c>LeaderboardPageSize</c> entries (ordered by snapshot rank), the caller's own rank/score
    /// (null if the caller has no entry in that league+event), and the league's total ranked count.
    /// </summary>
    Task<GauntletLeaderboardResponse> GetLeaderboardAsync(
        Guid eventId, GauntletLeague league, Guid callerId, CancellationToken ct = default);
}
