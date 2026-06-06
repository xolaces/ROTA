namespace ROTA.Domain.Entities;

/// <summary>
/// A one-way block: <see cref="BlockerId"/> no longer receives PMs from <see cref="BlockedId"/> (T37).
/// Exactly one row per ordered (blocker, blocked) pair.
/// </summary>
public class PlayerBlock
{
    private PlayerBlock() { }

    public static PlayerBlock Create(Guid blockerId, Guid blockedId)
        => new PlayerBlock
        {
            Id = Guid.NewGuid(),
            BlockerId = blockerId,
            BlockedId = blockedId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false,
        };

    public Guid Id { get; private set; }
    public Guid BlockerId { get; private set; }
    public Guid BlockedId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; }
}
