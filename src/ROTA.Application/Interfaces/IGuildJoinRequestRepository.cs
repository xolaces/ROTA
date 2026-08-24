using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

/// <summary>A pending join request joined to its applicant for display.</summary>
public sealed class GuildJoinRequestEntry
{
    public Guid Id { get; init; }
    public Guid GuildId { get; init; }
    public Guid PlayerId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int Level { get; init; }
    public ROTA.Domain.Enums.GuildJoinRequestKind Kind { get; init; }
    public ROTA.Domain.Enums.GuildJoinRequestStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public interface IGuildJoinRequestRepository
{
    Task<GuildJoinRequest?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Pending requests targeting a guild (officer review queue).</summary>
    Task<IReadOnlyList<GuildJoinRequest>> GetPendingForGuildAsync(Guid guildId, CancellationToken ct = default);

    /// <summary>
    /// Pending requests for a guild, JOINED to the applicant's player row so the officer review queue
    /// can show WHO is applying. The entity-only overload above carries just a PlayerId, which left the
    /// clients rendering a raw GUID in the applicant list.
    /// </summary>
    Task<IReadOnlyList<GuildJoinRequestEntry>> GetPendingForGuildWithPlayersAsync(Guid guildId, CancellationToken ct = default);

    /// <summary>Pending requests involving a player (their applications + invites awaiting their acceptance).</summary>
    Task<IReadOnlyList<GuildJoinRequest>> GetPendingForPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>The single pending request for (guild, player, kind), or null. Used for idempotency.</summary>
    Task<GuildJoinRequest?> FindPendingAsync(Guid guildId, Guid playerId, GuildJoinRequestKind kind, CancellationToken ct = default);

    Task<GuildJoinRequest> CreateAsync(GuildJoinRequest request, CancellationToken ct = default);
    Task UpdateAsync(GuildJoinRequest request, CancellationToken ct = default);
}
