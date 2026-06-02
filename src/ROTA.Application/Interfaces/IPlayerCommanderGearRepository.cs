using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerCommanderGearRepository
{
    /// <summary>Returns the commander-gear row for the player, including soft-deleted rows.</summary>
    Task<PlayerCommanderGear?> FindAsync(Guid playerId, CancellationToken ct = default);

    Task<PlayerCommanderGear> CreateAsync(PlayerCommanderGear row, CancellationToken ct = default);
    Task UpdateAsync(PlayerCommanderGear row, CancellationToken ct = default);
}
