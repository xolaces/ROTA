using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Redis-backed short-window chat throttle (exploit-audit finding J). INCR a per-player counter that
/// expires after the configured window; the send is allowed only while the count stays at or below the
/// cap. Same INCR + conditional EXPIRE shape as <see cref="AuthLockoutService"/> /
/// <see cref="SubmissionRateLimiter"/> and the HTTP RateLimitMiddleware.
///
/// FAIL OPEN on a Redis outage (mirrors RateLimitMiddleware): a throttle is a protection layer, not a
/// dependency — an unreachable Redis must not silence chat. Window/cap are config-driven
/// (<see cref="RateLimitConfig"/>) so play-pattern changes don't require a code change.
/// </summary>
public sealed class ChatRateLimiter : IChatRateLimiter
{
    private readonly IDatabase _redis;
    private readonly int _limit;
    private readonly TimeSpan _window;

    public ChatRateLimiter(IConnectionMultiplexer mux, IOptions<RateLimitConfig> config)
    {
        _redis = mux.GetDatabase();
        var cfg = config.Value;
        _limit = cfg.ChatMessagesPerWindow;
        _window = TimeSpan.FromSeconds(cfg.ChatWindowSeconds);
    }

    public async Task<bool> TryConsumeAsync(Guid playerId, CancellationToken ct = default)
    {
        var key = $"ratelimit:chat:player:{playerId}";
        try
        {
            var count = await _redis.StringIncrementAsync(key);
            if (count == 1)
                await _redis.KeyExpireAsync(key, _window);
            return count <= _limit;
        }
        catch (RedisException)
        {
            return true;
        }
        catch (RedisTimeoutException)
        {
            return true;
        }
    }
}
