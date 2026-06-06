namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// A directed friend request that becomes a mutual friendship on acceptance (T37). Exactly one row per
/// ordered (requester, addressee) pair; the service prevents duplicate/reverse pairs.
/// </summary>
public class Friendship
{
    private Friendship() { }

    public static Friendship Create(Guid requesterId, Guid addresseeId)
        => new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = requesterId,
            AddresseeId = addresseeId,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false,
        };

    public Guid Id { get; private set; }
    public Guid RequesterId { get; private set; }
    public Guid AddresseeId { get; private set; }
    public FriendshipStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; }

    /// <summary>Marks a pending request accepted.</summary>
    public void Accept()
    {
        Status = FriendshipStatus.Accepted;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Soft-deletes the friendship (unfriend / decline).</summary>
    public void Remove()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Involves the given player on either side of the pair.</summary>
    public bool Involves(Guid playerId) => RequesterId == playerId || AddresseeId == playerId;

    /// <summary>The other player in the pair relative to <paramref name="playerId"/>.</summary>
    public Guid OtherSide(Guid playerId) => RequesterId == playerId ? AddresseeId : RequesterId;
}
