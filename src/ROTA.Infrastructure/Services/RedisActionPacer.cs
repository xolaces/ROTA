using ROTA.Application.Interfaces;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// SET NX PX opens the window; a key that already exists means the window is still open, and its
/// PTTL is the wait. One round trip on the allowed path.
/// </summary>
public sealed class RedisActionPacer : IActionPacer
{
    private readonly IDatabase _redis;

    public RedisActionPacer(IConnectionMultiplexer mux) => _redis = mux.GetDatabase();

    public async Task<TimeSpan?> TryBeginAsync(string action, Guid playerId, TimeSpan minInterval, CancellationToken ct = default)
    {
        if (minInterval <= TimeSpan.Zero) return null;
        var key = $"pace:{action}:{playerId:N}";
        try
        {
            if (await _redis.StringSetAsync(key, 1, minInterval, When.NotExists))
                return null;
            var ttl = await _redis.KeyTimeToLiveAsync(key);
            // A key that expired between the two calls has no TTL: the window just closed.
            return ttl is { } left && left > TimeSpan.Zero ? left : null;
        }
        catch (RedisException) { return null; }
        catch (RedisTimeoutException) { return null; }
    }
}
