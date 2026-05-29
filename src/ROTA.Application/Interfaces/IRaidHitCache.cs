using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IRaidHitCache
{
    // Atomically reserves an idempotency slot using Redis SET NX.
    // (Acquired: true,  ExistingResponse: null)     — slot is ours; caller must call StoreResultAsync.
    // (Acquired: false, ExistingResponse: response) — a prior completed request exists; return it.
    // (Acquired: false, ExistingResponse: null)      — a concurrent in-flight request holds the slot.
    Task<(bool Acquired, RaidHitResponse? ExistingResponse)> TryAcquireSlotAsync(
        string idempotencyKey, CancellationToken ct = default);

    // Overwrites the placeholder written by TryAcquireSlotAsync with the real response.
    Task StoreResultAsync(string idempotencyKey, RaidHitResponse response, CancellationToken ct = default);
}
