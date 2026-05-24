using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA - Full implementation. Covers all IRefreshTokenRepository contract methods.
/// <summary>
/// EF Core implementation of IRefreshTokenRepository.
/// A token is "active" if is_revoked = false AND expires_at > UtcNow.
/// </summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly RotaDbContext _db;

    public RefreshTokenRepository(RotaDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    /// <inheritdoc />
    public async Task<int> CountActiveSessionsAsync(Guid playerId, CancellationToken ct = default)
        => await _db.RefreshTokens
            .CountAsync(t =>
                t.PlayerId == playerId &&
                !t.IsRevoked &&
                t.ExpiresAt > DateTimeOffset.UtcNow,
                ct);

    /// <inheritdoc />
    public async Task<RefreshToken?> FindOldestActiveAsync(Guid playerId, CancellationToken ct = default)
        => await _db.RefreshTokens
            .Where(t =>
                t.PlayerId == playerId &&
                !t.IsRevoked &&
                t.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);
        return token;
    }

    /// <inheritdoc />
    public async Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.Revoke();
        _db.RefreshTokens.Update(token);
        await _db.SaveChangesAsync(ct);
    }
}