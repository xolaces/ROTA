using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

public sealed class RaidHitCache : IRaidHitCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    // Sentinel stored while the request is in flight — cannot be valid JSON for RaidHitResponse.
    private const string Pending = "pending";

    private readonly IDatabase _db;

    public RaidHitCache(IConnectionMultiplexer mux)
    {
        _db = mux.GetDatabase();
    }

    // Atomic SET NX: closes the check-then-set race that existed between the old
    // GetAsync (line 190) and SetAsync (line 281) calls in RaidService.
    public async Task<(bool Acquired, RaidHitResponse? ExistingResponse)> TryAcquireSlotAsync(
        string idempotencyKey, CancellationToken ct = default)
    {
        var key = CacheKey(idempotencyKey);
        bool acquired = await _db.StringSetAsync(key, Pending, Ttl, When.NotExists);
        if (acquired)
            return (true, null);

        // Key already exists — read whatever is stored.
        var raw = await _db.StringGetAsync(key);
        if (!raw.HasValue || raw == Pending)
            return (false, null); // concurrent in-flight

        var response = JsonSerializer.Deserialize<RaidHitResponse>(raw.ToString());
        return (false, response);
    }

    // Overwrites the "pending" placeholder with the completed response.
    public async Task StoreResultAsync(string idempotencyKey, RaidHitResponse response, CancellationToken ct = default)
    {
        var serialized = JsonSerializer.Serialize(response);
        await _db.StringSetAsync(CacheKey(idempotencyKey), serialized, Ttl);
    }

    private static string CacheKey(string idempotencyKey) => $"raidhit:{idempotencyKey}";
}
