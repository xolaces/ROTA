using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

// BETA — Redis-backed. Key: raidhit:{idempotencyKey}, TTL: 24h.
public sealed class RaidHitCache : IRaidHitCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly IDatabase _db;

    public RaidHitCache(IConnectionMultiplexer mux)
    {
        _db = mux.GetDatabase();
    }

    public async Task<RaidHitResponse?> GetAsync(string idempotencyKey, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(CacheKey(idempotencyKey));
        if (!value.HasValue) return null;
        return JsonSerializer.Deserialize<RaidHitResponse>(value.ToString());
    }

    public async Task SetAsync(string idempotencyKey, RaidHitResponse response, CancellationToken ct = default)
    {
        var serialized = JsonSerializer.Serialize(response);
        await _db.StringSetAsync(CacheKey(idempotencyKey), serialized, Ttl);
    }

    private static string CacheKey(string idempotencyKey) => $"raidhit:{idempotencyKey}";
}
