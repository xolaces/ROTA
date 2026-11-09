// Refresh token issued alongside a JWT access token.
// Raw token is never stored � only a SHA256 hash. Rotated on every use.
using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

public class RefreshToken
{
    private RefreshToken() { }

    public RefreshToken(Guid playerId, string tokenHash, DateTimeOffset expiresAt, string? ipAddress)
    {
        PlayerId = playerId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        IpAddress = ipAddress;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PlayerId { get; private set; }
    public Player Player { get; private set; } = null!;

    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; } = false;

    // Why it was revoked, and when. Set together with IsRevoked (a check constraint holds the pair
    // in step). The reason decides what a later presentation of this token means; see
    // RefreshTokenRevokeReason.
    public RefreshTokenRevokeReason? RevokedReason { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsActive => !IsRevoked && ExpiresAt > DateTimeOffset.UtcNow;

    /// <summary>True only for a token consumed by a refresh. Presenting one again is the theft signal.</summary>
    public bool WasRotated => IsRevoked && RevokedReason == RefreshTokenRevokeReason.Rotated;

    // The first reason sticks. A rotated token swept up by a later revoke-all must still read as
    // Rotated, because that is what makes presenting it again a theft signal.
    public void Revoke(RefreshTokenRevokeReason reason)
    {
        if (IsRevoked) return;
        IsRevoked     = true;
        RevokedReason = reason;
        RevokedAt     = DateTimeOffset.UtcNow;
    }
}