namespace ROTA.Application.Interfaces;

/// <summary>
/// System 29 S1 — records that a player showed up today. Called on every successful authentication:
/// password login AND token refresh. The client resumes on a persisted refresh token and may never send
/// a password again, so when this bookkeeping lived in <c>LoginAsync</c> alone, a player who stayed
/// signed in stopped accumulating <c>DaysPlayed</c>.
/// </summary>
public interface ICheckInService
{
    /// <summary>
    /// Counts today (UTC) for the player if it hasn't been counted yet. Idempotent per day. May throw;
    /// callers on the auth path must treat it as best-effort.
    /// </summary>
    Task<CheckInResult> CheckInAsync(Guid playerId, CancellationToken ct = default);
}

/// <param name="NewDay">True when this call counted a new UTC day.</param>
/// <param name="PreviousDay">
/// The last day counted before this call. Null on a first-ever check-in or after a beta reset
/// (<c>Player.ResetProgress</c> clears it). System 30 derives the absence gap from it.
/// </param>
public sealed record CheckInResult(bool NewDay, DateOnly? PreviousDay);
