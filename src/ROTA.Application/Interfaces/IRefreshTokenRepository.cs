using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<int> CountActiveSessionsAsync(Guid playerId, CancellationToken ct = default);

    Task<RefreshToken?> FindOldestActiveAsync(Guid playerId, CancellationToken ct = default);

    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);

    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Atomic rotation latch — revokes the token via a single conditional UPDATE
    /// (token_hash matches AND not already revoked). Returns true only for the one caller
    /// that wins; a concurrent rotation of the same token returns false. This is what makes
    /// refresh-token rotation single-use under concurrency.
    /// </summary>
    Task<bool> TryRevokeAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Revokes all active sessions for the specified player in a single batch.</summary>
    Task RevokeAllActiveAsync(Guid playerId, CancellationToken ct = default);
}