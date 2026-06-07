using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — admin lifecycle for Gauntlet events. Used by the [AdminOnly]
// controller and the CLI. Settlement payout is Slice 5; here Settle transitions state only.
public interface IGauntletAdminService
{
    /// <summary>
    /// Opens a new event (Create → Activate). Enforces ≤1 Active — rejects if an Active event
    /// already exists. Audited.
    /// </summary>
    Task<GauntletEventActionResult> OpenEventAsync(
        string name, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken ct = default);

    /// <summary>Closes an event (guard: must be Active). Audited. (Slice 5 hooks settlement here.)</summary>
    Task<GauntletEventActionResult> CloseEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Transitions a Closed event to Settled (payout is Slice 5). Idempotent: a no-op returning the
    /// event summary if already Settled. Audited.
    /// </summary>
    Task<GauntletEventActionResult> SettleEventAsync(Guid eventId, CancellationToken ct = default);
}
