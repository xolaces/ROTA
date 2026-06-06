namespace ROTA.Domain.Enums;

/// <summary>State of a friendship request (T37).</summary>
public enum FriendshipStatus
{
    /// <summary>Requested by one side, awaiting the other's acceptance.</summary>
    Pending = 1,

    /// <summary>Mutually accepted — the two players are friends.</summary>
    Accepted = 2,
}
