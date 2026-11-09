using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
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

    public async Task RevokeAsync(RefreshToken token, RefreshTokenRevokeReason reason, CancellationToken ct = default)
    {
        token.Revoke(reason);
        _db.RefreshTokens.Update(token);
        await _db.SaveChangesAsync(ct);
    }

    // Single conditional UPDATE — bypasses the change tracker, so the check-and-set is atomic at
    // the database. Two concurrent rotations of the same token: exactly one sees rows == 1. The
    // reason and time are written in the same statement, so the check constraint never sees a
    // revoked row without a reason.
    public async Task<bool> TryMarkRotatedAsync(string tokenHash, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsRevoked, true)
                .SetProperty(t => t.RevokedReason, RefreshTokenRevokeReason.Rotated)
                .SetProperty(t => t.RevokedAt, now), ct);
        return rows == 1;
    }

    // A scalar projection is never tracked, so this reads what is committed now, not the stale
    // instance the caller loaded before it lost the latch.
    public async Task<RefreshTokenRevokeReason?> FindRevokeReasonAsync(string tokenHash, CancellationToken ct = default)
        => await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.IsRevoked)
            .Select(t => t.RevokedReason)
            .FirstOrDefaultAsync(ct);

    public async Task RevokeAllActiveAsync(Guid playerId, RefreshTokenRevokeReason reason, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.PlayerId == playerId && !t.IsRevoked && t.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        foreach (var t in active)
            t.Revoke(reason);

        if (active.Count > 0)
            await _db.SaveChangesAsync(ct);
    }
}