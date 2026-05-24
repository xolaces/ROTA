using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Data-access contract for the RefreshToken table.
/// Refresh token lifecycle: create, validate, rotate, revoke.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Looks up a refresh token by its SHA-256 hash.
    /// Raw token is never stored - only the hash. Returns null if not found.
    /// </summary>
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of non-revoked, non-expired refresh tokens for a player.
    /// Used to enforce the 3-session maximum.
    /// </summary>
    Task<int> CountActiveSessionsAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Returns the oldest active refresh token for a player.
    /// Called when a 4th session is created to evict the oldest.
    /// </summary>
    Task<RefreshToken?> FindOldestActiveAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Persists a newly issued refresh token.</summary>
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Marks a single token as revoked.
    /// Called during rotation (invalidates the consumed token) and on logout.
    /// </summary>
    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);
}