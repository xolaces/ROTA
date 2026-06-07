using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2 + 5) — admin lifecycle for Gauntlet events. Used by the [AdminOnly]
// controller and the CLI. Settle (Slice 5) distributes the rank-band prizes idempotently.
public interface IGauntletAdminService
{
    /// <summary>
    /// Opens a new event (Create → Activate). Enforces ≤1 Active — rejects if an Active event
    /// already exists. Audited.
    /// </summary>
    Task<GauntletEventActionResult> OpenEventAsync(
        string name, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken ct = default);

    /// <summary>Closes an event (guard: must be Active). Audited.</summary>
    Task<GauntletEventActionResult> CloseEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Settles a Closed event (Slice 5): recomputes ranks, then grants the per-rank-band prizes
    /// (Tokens + Pitchfork to the currency ledger, Trophies, honor-echo write-back on revoked
    /// event-magics) — every grant idempotent via a unique referenceId / unique index. Marks the
    /// event Settled only after all grants commit. Idempotent and re-runnable: a re-settle on an
    /// already-Settled event is a no-op that never throws or double-pays, returning a zero-count
    /// summary. The result's <c>Settlement</c> carries the payout counts.
    /// </summary>
    Task<GauntletEventActionResult> SettleEventAsync(Guid eventId, CancellationToken ct = default);
}
