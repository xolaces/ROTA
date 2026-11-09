using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<int> CountActiveSessionsAsync(Guid playerId, CancellationToken ct = default);

    Task<RefreshToken?> FindOldestActiveAsync(Guid playerId, CancellationToken ct = default);

    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>Revokes one token, recording why. A token that is already revoked keeps its first reason.</summary>
    Task RevokeAsync(RefreshToken token, RefreshTokenRevokeReason reason, CancellationToken ct = default);

    /// <summary>
    /// Atomic rotation latch — marks the token revoked as <see cref="RefreshTokenRevokeReason.Rotated"/>
    /// via a single conditional UPDATE (token_hash matches AND not already revoked). Returns true only
    /// for the one caller that wins; a concurrent rotation, logout or eviction of the same token returns
    /// false. This is what makes refresh-token rotation single-use under concurrency.
    /// </summary>
    Task<bool> TryMarkRotatedAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Reads why a token was revoked, straight from the database (not the change tracker). Used after
    /// losing the rotation latch, to tell a concurrent rotation (theft signal) from a concurrent logout
    /// or session-limit eviction (not one). Null when the token is unknown or not revoked.
    /// </summary>
    Task<RefreshTokenRevokeReason?> FindRevokeReasonAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Revokes all active sessions for the specified player in a single batch, recording why.
    /// Tokens already revoked are left alone, so a rotated token keeps reading as Rotated.
    /// </summary>
    Task RevokeAllActiveAsync(Guid playerId, RefreshTokenRevokeReason reason, CancellationToken ct = default);
}