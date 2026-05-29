using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<int> CountActiveSessionsAsync(Guid playerId, CancellationToken ct = default);

    Task<RefreshToken?> FindOldestActiveAsync(Guid playerId, CancellationToken ct = default);

    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);

    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>Revokes all active sessions for the specified player in a single batch.</summary>
    Task RevokeAllActiveAsync(Guid playerId, CancellationToken ct = default);
}