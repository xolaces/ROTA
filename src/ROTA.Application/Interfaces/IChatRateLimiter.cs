namespace ROTA.Application.Interfaces;

/// <summary>
/// Short-window per-player throttle for SignalR chat sends (exploit-audit finding J). The HTTP
/// <c>RateLimitMiddleware</c> never sees post-handshake hub frames, so the <c>ChatHub</c> calls this
/// before every world/raid/guild send. Redis-backed (INCR + EXPIRE keyed on the JWT sub); fails OPEN
/// on a Redis outage so a cache blip never silences chat.
/// </summary>
public interface IChatRateLimiter
{
    /// <summary>
    /// Consumes one unit against the caller's short-window chat counter. Returns false when the
    /// per-window cap is exceeded (the hub should drop the send). All chat scopes share one counter
    /// so a flood can't spread across world/raid/guild to dodge the cap.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid playerId, CancellationToken ct = default);
}
