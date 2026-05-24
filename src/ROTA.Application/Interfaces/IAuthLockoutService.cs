namespace ROTA.Application.Interfaces;

/// <summary>
/// Tracks failed login attempts per email and enforces lockout.
/// Backed by Redis so lockout state survives process restarts.
/// </summary>
public interface IAuthLockoutService
{
    /// <summary>Returns true if the account is currently locked out (≥5 failures within the window).</summary>
    Task<bool> IsLockedOutAsync(string email, CancellationToken ct = default);

    /// <summary>Increments the failure counter. Sets the 15-minute TTL on first failure.</summary>
    Task RecordFailedAttemptAsync(string email, CancellationToken ct = default);

    /// <summary>Clears the lockout counter on successful login.</summary>
    Task ClearAsync(string email, CancellationToken ct = default);
}
