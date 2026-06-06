using ROTA.Application.Interfaces;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Redis-backed hourly anti-spam limiter (T37/T38). Increments per-player and per-IP counters that
/// expire after one hour; a request is allowed only while both stay within their caps. Same INCR+EXPIRE
/// pattern as AuthLockoutService.
/// </summary>
public sealed class SubmissionRateLimiter : ISubmissionRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly IDatabase _redis;

    public SubmissionRateLimiter(IConnectionMultiplexer mux) => _redis = mux.GetDatabase();

    public async Task<bool> TryConsumeAsync(
        string action, Guid playerId, string? ipAddress,
        int perPlayerPerHour, int perIpPerHour, CancellationToken ct = default)
    {
        if (!await WithinLimitAsync($"ratelimit:submit:{action}:player:{playerId}", perPlayerPerHour))
            return false;

        if (!string.IsNullOrEmpty(ipAddress)
            && !await WithinLimitAsync($"ratelimit:submit:{action}:ip:{ipAddress}", perIpPerHour))
            return false;

        return true;
    }

    private async Task<bool> WithinLimitAsync(string key, int limit)
    {
        var count = await _redis.StringIncrementAsync(key);
        if (count == 1)
            await _redis.KeyExpireAsync(key, Window);
        return count <= limit;
    }
}
