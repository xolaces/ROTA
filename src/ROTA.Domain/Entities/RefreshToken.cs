// Refresh token issued alongside a JWT access token.
// Raw token is never stored — only a SHA256 hash. Rotated on every use.
namespace ROTA.Domain.Entities;

public class RefreshToken
{
    // Required by EF Core
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
    public string? IpAddress { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsActive => !IsRevoked && ExpiresAt > DateTimeOffset.UtcNow;

    /// <summary>Revokes this token, invalidating the session immediately.</summary>
    public void Revoke() => IsRevoked = true;
}