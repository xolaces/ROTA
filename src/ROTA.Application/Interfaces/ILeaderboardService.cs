using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

// BETA — read service for System 17 Global Leaderboards.
// Slice 3: discovery list + ranked-page with eligibility filtering.
// Slice 4: write hooks — RecordEnergySpendAsync / RecordRaidHitAsync.
// Eligibility predicate (server-enforced, applied in SQL):
//   - player.is_deleted = false
//   - player.is_banned = false
//   - player.level >= LeaderboardConfig.MinLevel (default 20)
//   - when ExcludeAdmins=true: player does NOT have PlayerRoles.Admin bit set
//   Moderators appear as regular players (not excluded).
public interface ILeaderboardService
{
    /// <summary>
    /// Returns the discovery list of all available boards: title, supported periods,
    /// and the current period_key for each board's active window.
    /// </summary>
    Task<List<LeaderboardSummary>> GetBoardsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a ranked page for the given board + period combo.
    /// <para>Validation failures (bad board/period combo, page &lt; 1, malformed periodKey) are
    /// returned as a failure result; the caller maps these to HTTP 400.</para>
    /// <para>When <paramref name="periodKey"/> is null the current period is resolved automatically.
    /// Past keys are always valid (retention = forever).</para>
    /// </summary>
    /// <param name="board">Which leaderboard.</param>
    /// <param name="period">Period granularity (must be legal for this board).</param>
    /// <param name="periodKey">Optional period key; null = current period.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="callerId">PlayerId from the JWT sub claim.</param>
    Task<LeaderboardPageResult> GetPageAsync(
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string? periodKey,
        int page,
        Guid callerId,
        CancellationToken ct = default);

    // ── Slice 4: write hooks ─────────────────────────────────────────────────

    /// <summary>
    /// Records an energy spend contribution to the EnergySpent Weekly and Monthly boards.
    /// Increments both period rows by <paramref name="amount"/>.
    /// <para>Called by <c>EnergyService.SpendEnergyAsync</c> after a successful atomic spend.
    /// The energy spend has NO ambient transaction at this point, so this method is
    /// best-effort: any exception is swallowed so the spend is never rolled back.</para>
    /// </summary>
    /// <param name="playerId">The player who spent energy.</param>
    /// <param name="amount">The energy amount spent (positive integer).</param>
    /// <param name="at">UTC timestamp of the spend — used to resolve the correct period keys.</param>
    Task RecordEnergySpendAsync(Guid playerId, long amount, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>
    /// Records a raid hit contribution to the DamageDealt Weekly/Monthly boards (increment)
    /// and the LargestHit Daily board (max-update).
    /// <para>MUST be called inside the advisory-lock transaction callback in
    /// <c>RaidService.HitRaidAsync</c>, immediately after <c>participant.RecordHit(damageFinal)</c>.
    /// Riding the ambient transaction makes the leaderboard increment atomic with the hit.
    /// It is intentionally NOT called on the Redis cached-replay path (the early-return before the
    /// advisory-lock block), so duplicate submissions do NOT double-count.</para>
    /// </summary>
    /// <param name="playerId">The player who landed the hit.</param>
    /// <param name="damageFinal">The final computed damage — reused directly, never recomputed.</param>
    /// <param name="at">UTC timestamp of the hit — used to resolve the correct period keys.</param>
    Task RecordRaidHitAsync(Guid playerId, long damageFinal, DateTimeOffset at, CancellationToken ct = default);
}

/// <summary>Discriminated result from <see cref="ILeaderboardService.GetPageAsync"/>.</summary>
public sealed class LeaderboardPageResult
{
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }
    public LeaderboardPageResponse? Page { get; private init; }

    public static LeaderboardPageResult Ok(LeaderboardPageResponse page)
        => new() { Success = true, Page = page };

    public static LeaderboardPageResult Fail(string message)
        => new() { Success = false, ErrorMessage = message };
}
