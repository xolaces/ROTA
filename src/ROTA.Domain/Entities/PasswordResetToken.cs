namespace ROTA.Domain.Entities;

/// <summary>
/// A single-use password-reset code (T65). Only the SHA256 hash of the code is stored — the plaintext
/// code travels to the player by email and is never persisted here. Consumption is an atomic
/// conditional UPDATE in the repository (the same race guard as BetaKey redemption).
/// </summary>
public class PasswordResetToken
{
    // Required by EF Core
    private PasswordResetToken() { }

    public static PasswordResetToken Create(Guid playerId, string codeHash, TimeSpan ttl)
        => new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            CodeHash = codeHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
            UsedAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }

    /// <summary>SHA256 hex of the normalized (uppercased, separators stripped) reset code.</summary>
    public string CodeHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Set exactly once by the consuming UPDATE — null means still redeemable (if unexpired).</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
}
