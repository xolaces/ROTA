using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<int> CountActiveSessionsAsync(Guid playerId, CancellationToken ct = default);

    Task<RefreshToken?> FindOldestActiveAsync(Guid playerId, CancellationToken ct = default);

    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);

    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);
}