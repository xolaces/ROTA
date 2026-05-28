using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IRaidHitCache
{
    Task<RaidHitResponse?> GetAsync(string idempotencyKey, CancellationToken ct = default);

    Task SetAsync(string idempotencyKey, RaidHitResponse response, CancellationToken ct = default);
}
