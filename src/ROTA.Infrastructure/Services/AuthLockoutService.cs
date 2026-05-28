using ROTA.Application.Interfaces;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

public sealed class AuthLockoutService : IAuthLockoutService
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IDatabase _redis;

    public AuthLockoutService(IConnectionMultiplexer mux)
    {
        _redis = mux.GetDatabase();
    }

    public async Task<bool> IsLockedOutAsync(string email, CancellationToken ct = default)
    {
        var key = LockoutKey(email);
        var value = await _redis.StringGetAsync(key);
        return value.HasValue && (int)value >= MaxAttempts;
    }

    public async Task RecordFailedAttemptAsync(string email, CancellationToken ct = default)
    {
        var key = LockoutKey(email);
        var count = await _redis.StringIncrementAsync(key);
        // Set TTL only on first failure so the window starts from the first bad attempt.
        if (count == 1)
            await _redis.KeyExpireAsync(key, LockoutDuration);
    }

    public async Task ClearAsync(string email, CancellationToken ct = default)
    {
        await _redis.KeyDeleteAsync(LockoutKey(email));
    }

    private static string LockoutKey(string email)
        => $"auth:lockout:{email.ToLowerInvariant()}";
}
