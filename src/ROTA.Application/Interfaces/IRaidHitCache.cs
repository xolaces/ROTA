using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Redis-backed idempotency cache for raid hits.
/// Duplicate submissions within the TTL window return the cached response with zero reprocessing.
/// </summary>
public interface IRaidHitCache
{
    /// <summary>Returns the cached RaidHitResponse for the given idempotency key, or null on cache miss.</summary>
    Task<RaidHitResponse?> GetAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>Stores a RaidHitResponse under the given idempotency key with a 24-hour TTL.</summary>
    Task SetAsync(string idempotencyKey, RaidHitResponse response, CancellationToken ct = default);
}
