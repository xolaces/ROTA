using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerMagicRepository
{
    Task<IReadOnlyList<PlayerMagic>> GetOwnedAsync(Guid playerId, CancellationToken ct = default);
    Task<PlayerMagic?> FindAsync(Guid playerId, string magicDefinitionId, CancellationToken ct = default);
    Task UpsertAsync(Guid playerId, string magicDefinitionId, CancellationToken ct = default);
}
