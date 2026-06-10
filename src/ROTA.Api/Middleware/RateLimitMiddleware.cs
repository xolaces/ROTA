using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using StackExchange.Redis;

namespace ROTA.Api.Middleware;

// Per-IP:     auth endpoints     → AuthRequestsPerWindow   (key: ratelimit:ip:{ip}:{path})
// Per-player: authenticated      → PlayerRequestsPerWindow (key: ratelimit:player:{playerId})
// Per-IP:     unauthenticated    → PlayerRequestsPerWindow (key: ratelimit:ip:{ip}:anon)
// Ceilings + window are config-driven (RateLimitConfig); defaults 10 auth / 180 player per 60s.
//
// AUDIT FIX — runs AFTER UseAuthentication and keys the per-player limit on the VERIFIED identity
// (context.User). The previous design read the JWT 'sub' without signature validation, so an
// attacker could forge a structurally-valid token carrying a victim's GUID and exhaust the victim's
// bucket (targeted denial-of-service). Unauthenticated non-auth traffic now falls back to a per-IP
// bucket (it was previously not limited at all).
public class RateLimitMiddleware
{
    private readonly int _authLimit;
    private readonly int _playerLimit;
    private readonly TimeSpan _window;

    private readonly RequestDelegate _next;
    private readonly IDatabase _redis;
    private readonly IServiceScopeFactory _scopeFactory;

    public RateLimitMiddleware(
        RequestDelegate next,
        IConnectionMultiplexer mux,
        IServiceScopeFactory scopeFactory,
        IOptions<RateLimitConfig> config)
    {
        _next = next;
        _redis = mux.GetDatabase();
        _scopeFactory = scopeFactory;

        var cfg = config.Value;
        _authLimit = cfg.AuthRequestsPerWindow;
        _playerLimit = cfg.PlayerRequestsPerWindow;
        _window = TimeSpan.FromSeconds(cfg.WindowSeconds);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            // Per-IP limit for auth endpoints
            var segment = path.Replace('/', '_').Trim('_').ToLowerInvariant();
            var key = $"ratelimit:ip:{ip}:{segment}";

            if (await IsLimitExceededAsync(key, _authLimit))
            {
                await WriteLimitBreachAuditAsync(null, $"RateLimitIp:{path}", ip);
                await WriteRateLimitResponse(context, key);
                return;
            }
        }
        else
        {
            // Per-player limit for game action endpoints — VERIFIED identity only. A request whose
            // token failed validation is anonymous here and must never consume a player's bucket.
            var playerId = TryExtractVerifiedPlayerId(context);
            if (playerId is not null)
            {
                var key = $"ratelimit:player:{playerId}";
                if (await IsLimitExceededAsync(key, _playerLimit))
                {
                    await WriteLimitBreachAuditAsync(Guid.Parse(playerId), $"RateLimitPlayer:{path}", ip);
                    await WriteRateLimitResponse(context, key);
                    return;
                }
            }
            else
            {
                // Unauthenticated traffic to game routes (junk/forged tokens, scanners) — bound it
                // per-IP so it can't hammer the API for free before authorization rejects it.
                var key = $"ratelimit:ip:{ip}:anon";
                if (await IsLimitExceededAsync(key, _playerLimit))
                {
                    await WriteLimitBreachAuditAsync(null, $"RateLimitAnon:{path}", ip);
                    await WriteRateLimitResponse(context, key);
                    return;
                }
            }
        }

        await _next(context);
    }

    // INCR + conditional EXPIRE: counter expires after one window regardless of traffic shape.
    // Audit fix — FAIL OPEN on Redis outage: an unreachable Redis previously threw out of the
    // middleware and 500'd every request, turning a cache outage into a full API outage. Rate
    // limiting is a protection layer, not a dependency — degrade to "not limited" and keep serving.
    private async Task<bool> IsLimitExceededAsync(string key, int limit)
    {
        try
        {
            var count = await _redis.StringIncrementAsync(key);
            if (count == 1)
                await _redis.KeyExpireAsync(key, _window);
            return count > limit;
        }
        catch (RedisException)
        {
            return false;
        }
        catch (RedisTimeoutException)
        {
            return false;
        }
    }

    private async Task WriteRateLimitResponse(HttpContext context, string key)
    {
        var ttl = await _redis.KeyTimeToLiveAsync(key);
        var retryAfter = (int)Math.Ceiling(ttl?.TotalSeconds ?? _window.TotalSeconds);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = retryAfter.ToString();
        context.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new { message = "Too many requests. Please slow down." });
        await context.Response.WriteAsync(body);
    }

    private async Task WriteLimitBreachAuditAsync(Guid? playerId, string action, string ip)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            await auditLog.AppendAsync(AuditLog.Create(playerId, action, null, "Rate limit exceeded", ip));
        }
        catch
        {
            // Audit failure must never break the request pipeline.
        }
    }

    // The 'sub' claim from the SIGNATURE-VERIFIED principal populated by UseAuthentication.
    // Never read identity from the raw header here — an unverified sub lets an attacker burn a
    // victim's rate-limit bucket with forged tokens.
    private static string? TryExtractVerifiedPlayerId(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return null;

        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(sub, out _) ? sub : null;
    }
}
