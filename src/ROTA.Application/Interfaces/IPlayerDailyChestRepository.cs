using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>System 29 — persistence for <see cref="PlayerDailyChest"/>. Both writes are race-safe in SQL.</summary>
public interface IPlayerDailyChestRepository
{
    Task<PlayerDailyChest?> FindAsync(Guid playerId, DateOnly chestDay, CancellationToken ct = default);

    /// <summary>
    /// Inserts the chest unless one already exists for (player, day). Two first reads in the same instant
    /// both roll, but only one roll is stored; callers re-read afterwards and use whatever won.
    /// Returns true only for the caller whose row was stored.
    /// </summary>
    Task<bool> InsertIfAbsentAsync(PlayerDailyChest chest, CancellationToken ct = default);

    /// <summary>
    /// Claims the pick with a conditional UPDATE (<c>picked_index IS NULL</c>). Returns true for exactly one
    /// caller per chest. Enlists in the ambient transaction, so a failed grant afterwards rolls it back.
    /// </summary>
    Task<bool> TryPickAsync(Guid chestId, Guid playerId, short index, CancellationToken ct = default);
}
