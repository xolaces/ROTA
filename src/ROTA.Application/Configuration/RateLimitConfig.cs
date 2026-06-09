namespace ROTA.Application.Configuration;

// Rate-limit ceilings bound from appsettings "RateLimitConfig" via IOptions<RateLimitConfig>.
// Per-IP limit guards the auth endpoints; per-player limit guards every other game route.
// Tunable so play-pattern changes (e.g. raid auto-hit, fast questing) don't require a code change.
public class RateLimitConfig
{
    /// <summary>
    /// Max requests per IP per window on /api/auth/** (login/refresh abuse guard). Default 10.
    /// </summary>
    public int AuthRequestsPerWindow { get; set; } = 10;

    /// <summary>
    /// Max requests per player per window on all non-auth game routes. Default 180 (3/sec) — sized so
    /// fast questing and 0.70s raid auto-hit (~85/min) never trip the limiter under normal play.
    /// </summary>
    public int PlayerRequestsPerWindow { get; set; } = 180;

    /// <summary>Sliding window length in seconds. Default 60.</summary>
    public int WindowSeconds { get; set; } = 60;
}
