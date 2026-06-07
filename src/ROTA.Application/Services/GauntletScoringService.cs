using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA (System 16 Slice 3) — scoring aggregation + per-league snapshot rank + leaderboard read.
// The authority for score is always the participant damage written by HitRaidAsync; GauntletEntry.Score
// is a read-optimised projection updated per qualifying hit (the write hook is wired by Slice 4). This
// service introduces NO combat math — it increments the denormalised total, snapshots ranks, and reads
// the snapshot back. All ranking/ordering/isolation is enforced in SQL by the repository.
public sealed class GauntletScoringService : IGauntletScoringService
{
    private readonly IGauntletEntryRepository _entries;
    private readonly GauntletConfig _config;

    public GauntletScoringService(
        IGauntletEntryRepository entries,
        IOptions<GauntletConfig> config)
    {
        _entries = entries;
        _config  = config.Value;
    }

    public Task UpdateScoreAsync(
        Guid playerId, Guid eventId, long deltaScore, DateTimeOffset hitAt, CancellationToken ct = default)
        // Atomic single UPDATE in the repo; tie_break_at only advances on a positive delta.
        => _entries.IncrementScoreAsync(eventId, playerId, deltaScore, hitAt, ct);

    public Task RecomputeRanksAsync(Guid eventId, CancellationToken ct = default)
        => _entries.RecomputeRanksAsync(eventId, ct);

    public async Task<GauntletLeaderboardResponse> GetLeaderboardAsync(
        Guid eventId, GauntletLeague league, Guid callerId, CancellationToken ct = default)
    {
        var page = await _entries.GetLeaderboardPageAsync(
            eventId, league, _config.LeaderboardPageSize, ct);

        var totalRanked = await _entries.CountRankedAsync(eventId, league, ct);

        // Caller's own standing — scoped to this league so a player whose locked league differs
        // does not surface a rank on the wrong board (null in that case).
        var mine = await _entries.GetRankAndScoreAsync(eventId, callerId, league, ct);

        return new GauntletLeaderboardResponse
        {
            League      = league.ToString(),
            Entries     = page.Select(r => new GauntletLeaderboardEntryDto
            {
                Rank        = r.Rank,
                PlayerId    = r.PlayerId,
                DisplayName = r.DisplayName,
                Score       = r.Score,
            }).ToList(),
            YourRank    = mine?.Rank,
            YourScore   = mine?.Score,
            TotalRanked = totalRanked,
        };
    }
}
