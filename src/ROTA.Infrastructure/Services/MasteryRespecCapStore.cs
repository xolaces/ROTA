using ROTA.Application.Interfaces;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Redis-backed weekly paid-re-spec cap (System 22 Phase A, Slice 3). Key
/// <c>respec:paid:week:{playerId}</c> is set on use and expires at the next Monday 00:00 UTC, so the
/// gate aligns with the ISO-week gem-ledger bucket. Mirrors <c>AuthLockoutService</c>'s key+TTL idiom.
/// </summary>
public sealed class MasteryRespecCapStore : IMasteryRespecCapStore
{
    private readonly IDatabase _redis;

    public MasteryRespecCapStore(IConnectionMultiplexer mux) => _redis = mux.GetDatabase();

    private static string Key(Guid playerId) => $"respec:paid:week:{playerId}";

    public async Task<bool> IsPaidWeeklyUsedAsync(Guid playerId, CancellationToken ct = default)
        => (await _redis.StringGetAsync(Key(playerId))).HasValue;

    public Task MarkPaidWeeklyUsedAsync(Guid playerId, CancellationToken ct = default)
    {
        var utcNow = DateTimeOffset.UtcNow.UtcDateTime;
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)utcNow.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        var nextMonday = utcNow.Date.AddDays(daysUntilMonday);
        return _redis.StringSetAsync(Key(playerId), "1", nextMonday - utcNow);
    }
}
