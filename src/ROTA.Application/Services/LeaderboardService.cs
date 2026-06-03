using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA — read service for System 17 Global Leaderboards (Slice 3).
// Eligibility (banned/soft-deleted/level/Admin) is enforced in SQL via the repository
// so that page offsets and rank counts are always correct.
public sealed class LeaderboardService : ILeaderboardService
{
    private readonly ILeaderboardEntryRepository _repo;
    private readonly IPlayerRepository _players;
    private readonly IPeriodKeyResolver _keyResolver;
    private readonly IOptions<LeaderboardConfig> _cfg;

    // Static board metadata: titles and valid period granularities per board.
    // Locked decisions: LargestHit→Daily, Stat*→Live, EnergySpent/DamageDealt→Weekly|Monthly.
    private static readonly IReadOnlyDictionary<LeaderboardBoard, BoardMeta> AllBoards =
        new Dictionary<LeaderboardBoard, BoardMeta>
        {
            [LeaderboardBoard.StatAttack]      = new("Strongest Attackers",  new[] { LeaderboardPeriod.Live }),
            [LeaderboardBoard.StatDefense]     = new("Strongest Defenders",  new[] { LeaderboardPeriod.Live }),
            [LeaderboardBoard.StatDiscernment] = new("Highest Discernment",  new[] { LeaderboardPeriod.Live }),
            [LeaderboardBoard.EnergySpent]     = new("Top Questers",         new[] { LeaderboardPeriod.Weekly, LeaderboardPeriod.Monthly }),
            [LeaderboardBoard.DamageDealt]     = new("Top Raiders",          new[] { LeaderboardPeriod.Weekly, LeaderboardPeriod.Monthly }),
            [LeaderboardBoard.LargestHit]      = new("Largest Single Hit",   new[] { LeaderboardPeriod.Daily }),
        };

    public LeaderboardService(
        ILeaderboardEntryRepository repo,
        IPlayerRepository players,
        IPeriodKeyResolver keyResolver,
        IOptions<LeaderboardConfig> cfg)
    {
        _repo        = repo;
        _players     = players;
        _keyResolver = keyResolver;
        _cfg         = cfg;
    }

    // ── GetBoardsAsync ────────────────────────────────────────────────────────

    public Task<List<LeaderboardSummary>> GetBoardsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var summaries = AllBoards.Select(kvp =>
        {
            var (board, meta) = (kvp.Key, kvp.Value);
            // For multi-period boards (EnergySpent/DamageDealt) the current key uses Weekly
            // as the "primary" period for the discovery list; clients can query either.
            var currentPeriod = meta.Periods[0];
            var currentKey    = _keyResolver.Resolve(now, currentPeriod);
            return new LeaderboardSummary
            {
                Board            = board.ToString(),
                Title            = meta.Title,
                Periods          = meta.Periods.Select(p => p.ToString()).ToList(),
                CurrentPeriodKey = currentKey,
            };
        }).ToList();

        return Task.FromResult(summaries);
    }

    // ── GetPageAsync ──────────────────────────────────────────────────────────

    public async Task<LeaderboardPageResult> GetPageAsync(
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string? periodKey,
        int page,
        Guid callerId,
        CancellationToken ct = default)
    {
        // ── Validate page ────────────────────────────────────────────────────
        if (page < 1)
            return LeaderboardPageResult.Fail("Page must be 1 or greater.");

        // ── Validate board/period combo ──────────────────────────────────────
        if (!AllBoards.TryGetValue(board, out var meta))
            return LeaderboardPageResult.Fail($"Unknown board '{board}'.");

        if (!meta.Periods.Contains(period))
            return LeaderboardPageResult.Fail(
                $"Board '{board}' does not support period '{period}'. " +
                $"Valid periods: {string.Join(", ", meta.Periods)}.");

        // ── Validate / resolve periodKey ─────────────────────────────────────
        string finalKey;
        if (periodKey != null)
        {
            var validated = ValidatePeriodKeyFormat(periodKey, period);
            if (validated == null)
                return LeaderboardPageResult.Fail(
                    $"Period key '{periodKey}' does not match the expected format for period '{period}'.");
            finalKey = validated;
        }
        else
        {
            finalKey = _keyResolver.Resolve(DateTimeOffset.UtcNow, period);
        }

        // ── Config ───────────────────────────────────────────────────────────
        var cfg        = _cfg.Value;
        var pageSize   = cfg.PageSize;
        var minLevel   = cfg.MinLevel;
        var excludeAdm = cfg.ExcludeAdmins;

        // ── Read the eligible ranked page ────────────────────────────────────
        var entries = await _repo.GetEligiblePageAsync(
            board, finalKey, page, pageSize, minLevel, excludeAdm, ct);

        var totalRanked = await _repo.CountEligibleAsync(
            board, finalKey, minLevel, excludeAdm, ct);

        // ── Assign contiguous ranks (1-based, accounting for page offset) ────
        var offset  = (page - 1) * pageSize;
        var dtoList = entries.Select((e, i) => new LeaderboardEntryDto
        {
            Rank        = offset + i + 1,
            PlayerId    = e.PlayerId,
            DisplayName = ResolveDisplayName(e.DisplayName, e.Username),
            Value       = e.Value,
        }).ToList();

        // ── Caller rank ──────────────────────────────────────────────────────
        var callerRank = await _repo.GetCallerRankAsync(
            callerId, board, finalKey, minLevel, excludeAdm, ct);

        LeaderboardEntryDto? youDto = null;
        if (callerRank != null)
        {
            // Prefer the display name from the page (already hydrated) when the caller is on it.
            var onPage   = dtoList.FirstOrDefault(e => e.PlayerId == callerId);
            var dispName = onPage?.DisplayName ?? await ResolveCallerNameAsync(callerId, ct);

            youDto = new LeaderboardEntryDto
            {
                Rank        = callerRank.Rank,
                PlayerId    = callerRank.PlayerId,
                DisplayName = dispName,
                Value       = callerRank.Value,
            };
        }

        return LeaderboardPageResult.Ok(new LeaderboardPageResponse
        {
            Board       = board.ToString(),
            Period      = period.ToString(),
            PeriodKey   = finalKey,
            Page        = page,
            PageSize    = pageSize,
            TotalRanked = totalRanked,
            Entries     = dtoList,
            You         = youDto,
        });
    }

    // ── Slice 4: write hooks ──────────────────────────────────────────────────

    public async Task RecordEnergySpendAsync(Guid playerId, long amount, DateTimeOffset at, CancellationToken ct = default)
    {
        var weekKey  = _keyResolver.Resolve(at, LeaderboardPeriod.Weekly);
        var monthKey = _keyResolver.Resolve(at, LeaderboardPeriod.Monthly);

        await _repo.IncrementAsync(playerId, LeaderboardBoard.EnergySpent, LeaderboardPeriod.Weekly,  weekKey,  amount, at, ct);
        await _repo.IncrementAsync(playerId, LeaderboardBoard.EnergySpent, LeaderboardPeriod.Monthly, monthKey, amount, at, ct);
    }

    public async Task RecordRaidHitAsync(Guid playerId, long damageFinal, DateTimeOffset at, CancellationToken ct = default)
    {
        var weekKey  = _keyResolver.Resolve(at, LeaderboardPeriod.Weekly);
        var monthKey = _keyResolver.Resolve(at, LeaderboardPeriod.Monthly);
        var dayKey   = _keyResolver.Resolve(at, LeaderboardPeriod.Daily);

        await _repo.IncrementAsync(playerId,   LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly,  weekKey,  damageFinal, at, ct);
        await _repo.IncrementAsync(playerId,   LeaderboardBoard.DamageDealt, LeaderboardPeriod.Monthly, monthKey, damageFinal, at, ct);
        await _repo.MaxUpdateAsync(playerId,   LeaderboardBoard.LargestHit,  LeaderboardPeriod.Daily,   dayKey,   damageFinal, at, ct);
    }

    // ── Slice 5: Stat board snapshot ─────────────────────────────────────────

    public async Task<int> SnapshotStatBoardAsync(CancellationToken ct = default)
    {
        var cfg = _cfg.Value;
        var now = DateTimeOffset.UtcNow;

        // Retrieve every eligible player's raw stat values in one SQL round-trip.
        var snapshots = await _repo.GetEligibleStatSnapshotAsync(
            cfg.MinLevel, cfg.ExcludeAdmins, ct);

        // Overwrite all three Stat board rows for every eligible player.
        // period = Live, period_key = "live" for all three.
        const string liveKey = "live";
        foreach (var snap in snapshots)
        {
            await _repo.SetValueAsync(
                snap.PlayerId, LeaderboardBoard.StatAttack, LeaderboardPeriod.Live,
                liveKey, snap.BaseAttack, now, ct);

            await _repo.SetValueAsync(
                snap.PlayerId, LeaderboardBoard.StatDefense, LeaderboardPeriod.Live,
                liveKey, snap.BaseDefense, now, ct);

            await _repo.SetValueAsync(
                snap.PlayerId, LeaderboardBoard.StatDiscernment, LeaderboardPeriod.Live,
                liveKey, snap.DiscernmentInvestment, now, ct);
        }

        // Newly-ineligible players: their stale Live rows are filtered out by the read join
        // (GetEligiblePageAsync) — no purge is necessary.
        return snapshots.Count;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates that a supplied period_key string matches the expected format for the given period.
    /// Returns the key unchanged if valid, or null if the format does not match.
    /// </summary>
    private static string? ValidatePeriodKeyFormat(string key, LeaderboardPeriod period)
        => period switch
        {
            LeaderboardPeriod.Live    => key == "live" ? key : null,
            LeaderboardPeriod.Daily   => IsMatch(key, "day:",   "day:yyyy-MM-dd"),
            LeaderboardPeriod.Weekly  => IsMatch(key, "week:",  null),
            LeaderboardPeriod.Monthly => IsMatch(key, "month:", "month:yyyy-MM"),
            _                         => null,
        };

    private static string? IsMatch(string key, string prefix, string? exactLengthTemplate)
    {
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        if (exactLengthTemplate != null && key.Length != exactLengthTemplate.Length)
            return null;
        return key;
    }

    private static string ResolveDisplayName(string displayName, string username)
        => string.IsNullOrWhiteSpace(displayName) ? username : displayName;

    /// <summary>
    /// Fetches the caller's display name from the players table when they are off the page.
    /// Small extra query, only called once per request when needed.
    /// </summary>
    private async Task<string> ResolveCallerNameAsync(Guid callerId, CancellationToken ct)
    {
        var player = await _players.FindByIdAsync(callerId, ct);
        if (player is null) return string.Empty;
        return ResolveDisplayName(player.DisplayName, player.Username);
    }
}

// ── Internal metadata record ─────────────────────────────────────────────────

internal sealed record BoardMeta(string Title, LeaderboardPeriod[] Periods);
