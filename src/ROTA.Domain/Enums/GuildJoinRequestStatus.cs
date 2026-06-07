namespace ROTA.Domain.Enums;

/// <summary>
/// Lifecycle state of a guild join request (System 21 Slice 1). A request begins
/// <see cref="Pending"/> and terminates in exactly one of the other states.
/// </summary>
public enum GuildJoinRequestStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Withdrawn = 3,
    Expired = 4,
}
