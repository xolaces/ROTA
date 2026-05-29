using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly RotaDbContext _db;

    public RefreshTokenRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<int> CountActiveSessionsAsync(Guid playerId, CancellationToken ct = default)
        => await _db.RefreshTokens
            .CountAsync(t =>
                t.PlayerId == playerId &&
                !t.IsRevoked &&
                t.ExpiresAt > DateTimeOffset.UtcNow,
                ct);

    public async Task<RefreshToken?> FindOldestActiveAsync(Guid playerId, CancellationToken ct = default)
        => await _db.RefreshTokens
            .Where(t =>
                t.PlayerId == playerId &&
                !t.IsRevoked &&
                t.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.Revoke();
        _db.RefreshTokens.Update(token);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllActiveAsync(Guid playerId, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.PlayerId == playerId && !t.IsRevoked && t.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        foreach (var t in active)
            t.Revoke();

        if (active.Count > 0)
            await _db.SaveChangesAsync(ct);
    }
}