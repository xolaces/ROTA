namespace ROTA.Application.Interfaces;

/// <summary>
/// Per-action anti-spam limiter for player-submitted content (bug/ticket submissions T38, reports T37).
/// Redis-backed; enforces a per-player AND per-IP hourly cap.
/// </summary>
public interface ISubmissionRateLimiter
{
    /// <summary>
    /// Consumes one unit against the per-player and per-IP hourly counters for <paramref name="action"/>.
    /// Returns false when either cap is exceeded (caller should reject with 429).
    /// </summary>
    Task<bool> TryConsumeAsync(
        string action, Guid playerId, string? ipAddress,
        int perPlayerPerHour, int perIpPerHour, CancellationToken ct = default);
}
