using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IMagicService
{
    Task<IReadOnlyList<OwnedMagicResponse>> GetOwnedMagicsAsync(Guid playerId, CancellationToken ct = default);
}
