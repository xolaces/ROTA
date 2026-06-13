using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>Persistence for a player's dedicated Gauntlet battalion (System 24 D8). One per player.</summary>
public interface IGauntletBattalionRepository
{
    Task<PlayerGauntletBattalion?> GetForPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Insert a new battalion or persist mutations to an existing (tracked) one.</summary>
    Task UpsertAsync(PlayerGauntletBattalion battalion, CancellationToken ct = default);
}
