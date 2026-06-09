using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using StackExchange.Redis;

namespace ROTA.Api.Middleware;

// Per-IP:     auth endpoints   → AuthRequestsPerWindow   (key: ratelimit:ip:{ip}:{path})
// Per-player: all other routes → PlayerRequestsPerWindow (key: ratelimit:player:{playerId})
// Ceilings + window are config-driven (RateLimitConfig); defaults 10 auth / 180 player per 60s.
// Runs before JWT validation; player ID is extracted without signature verification
// (auth middleware rejects invalid tokens anyway).
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
            // Per-player limit for game action endpoints (read sub from JWT without validating)
            var playerId = TryExtractPlayerId(context);
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
        }

        await _next(context);
    }

    // INCR + conditional EXPIRE: counter expires after one window regardless of traffic shape.
    private async Task<bool> IsLimitExceededAsync(string key, int limit)
    {
        var count = await _redis.StringIncrementAsync(key);
        if (count == 1)
            await _redis.KeyExpireAsync(key, _window);
        return count > limit;
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

    // Extract the 'sub' claim from the Bearer token without signature validation.
    // The auth middleware will reject invalid tokens — we only use this for rate-limiting.
    private static string? TryExtractPlayerId(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.FirstOrDefault();
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var raw = header["Bearer ".Length..].Trim();
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(raw)) return null;

            var jwt = handler.ReadJwtToken(raw);
            return jwt.Subject; // 'sub' claim
        }
        catch
        {
            return null;
        }
    }
}
