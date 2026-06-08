using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerMasteryActivityRepository
{
    Task<IReadOnlyList<PlayerMasteryActivity>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default);

    // Slice 4 adds: IncrementAsync (race-safe ON CONFLICT upsert) + TryRecordEventAsync (idempotency ledger).
}
