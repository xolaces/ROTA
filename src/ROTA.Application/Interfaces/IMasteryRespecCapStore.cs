namespace ROTA.Application.Interfaces;

/// <summary>
/// The weekly paid-re-spec cap gate (System 22 Phase A, Slice 3). Redis-backed in Infrastructure
/// (mirrors <c>ISubmissionRateLimiter</c> / <c>IAuthLockoutService</c>). The gem-ledger week-bucket
/// referenceId is the hard backstop; this is the cheap fast-reject gate the design calls for.
/// </summary>
public interface IMasteryRespecCapStore
{
    /// <summary>True if the player's paid re-spec slot for the current week is already marked used.</summary>
    Task<bool> IsPaidWeeklyUsedAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Marks the paid weekly slot used; the key expires at the next Monday 00:00 UTC.</summary>
    Task MarkPaidWeeklyUsedAsync(Guid playerId, CancellationToken ct = default);
}
