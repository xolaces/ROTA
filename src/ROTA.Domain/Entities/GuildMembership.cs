namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// A player's membership in a guild (System 21 Slice 1). One ACTIVE membership per player is enforced
/// by a partial unique index on <c>player_id WHERE is_deleted = false</c> (the friendship lesson — a
/// soft-deleted row lingers after leaving, so re-joining inserts a fresh row without colliding).
/// <see cref="LastActiveAt"/> drives inactivity succession.
/// </summary>
public class GuildMembership
{
    // Required by EF Core
    private GuildMembership() { }

    public static GuildMembership Create(Guid guildId, Guid playerId, GuildRank rank)
    {
        var now = DateTimeOffset.UtcNow;
        return new GuildMembership
        {
            Id = Guid.NewGuid(),
            GuildId = guildId,
            PlayerId = playerId,
            Rank = rank,
            ContributionTotal = 0,
            JoinedAt = now,
            LastActiveAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        };
    }

    public Guid Id { get; private set; }
    public Guid GuildId { get; private set; }
    public Guid PlayerId { get; private set; }
    public GuildRank Rank { get; private set; }

    /// <summary>Running contribution total (e.g. damage). Accrual lands in Slice 3; default 0.</summary>
    public long ContributionTotal { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>Last activity instant — the input to inactivity succession (decision §5.6).</summary>
    public DateTimeOffset LastActiveAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; }

    public void SetRank(GuildRank rank)
    {
        Rank = rank;
        Touch();
    }

    public void AddContribution(long amount)
    {
        if (amount <= 0) return;
        ContributionTotal += amount;
        Touch();
    }

    /// <summary>Marks the member as active now (refreshes the succession clock).</summary>
    public void TouchActivity()
    {
        LastActiveAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
