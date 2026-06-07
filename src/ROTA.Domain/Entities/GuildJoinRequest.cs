namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// A pending request to join a guild (System 21 Slice 1). <see cref="GuildJoinRequestKind.Application"/>
/// is player→guild (an officer+ accepts); <see cref="GuildJoinRequestKind.Invite"/> is officer+→player
/// (the invited player accepts). Terminates in Accepted/Rejected/Withdrawn/Expired.
/// </summary>
public class GuildJoinRequest
{
    // Required by EF Core
    private GuildJoinRequest() { }

    public static GuildJoinRequest Create(Guid guildId, Guid playerId, GuildJoinRequestKind kind)
    {
        var now = DateTimeOffset.UtcNow;
        return new GuildJoinRequest
        {
            Id = Guid.NewGuid(),
            GuildId = guildId,
            PlayerId = playerId,
            Kind = kind,
            Status = GuildJoinRequestStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        };
    }

    public Guid Id { get; private set; }
    public Guid GuildId { get; private set; }
    public Guid PlayerId { get; private set; }
    public GuildJoinRequestKind Kind { get; private set; }
    public GuildJoinRequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; }

    // ── Domain methods ────────────────────────────────────────────────────────

    public void Accept() => SetStatus(GuildJoinRequestStatus.Accepted);
    public void Reject() => SetStatus(GuildJoinRequestStatus.Rejected);
    public void Withdraw() => SetStatus(GuildJoinRequestStatus.Withdrawn);
    public void Expire() => SetStatus(GuildJoinRequestStatus.Expired);

    private void SetStatus(GuildJoinRequestStatus status)
    {
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
