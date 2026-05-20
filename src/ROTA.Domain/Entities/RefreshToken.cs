namespace ROTA.Domain.Entities;

/// <summary>
/// Refresh token issued alongside a JWT access token.
/// Tokens are rotated on every use — the old token is revoked immediately.
/// Max active tokens per player enforced at service layer (max 3 sessions).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid PlayerId { get; private set; }
    public Player Player { get; private set; } = null!;

    /// <summary>
    /// Hashed token value — raw token is never stored.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; } = false;

    public string? IpAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}