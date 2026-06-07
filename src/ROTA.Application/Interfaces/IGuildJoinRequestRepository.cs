using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IGuildJoinRequestRepository
{
    Task<GuildJoinRequest?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Pending requests targeting a guild (officer review queue).</summary>
    Task<IReadOnlyList<GuildJoinRequest>> GetPendingForGuildAsync(Guid guildId, CancellationToken ct = default);

    /// <summary>Pending requests involving a player (their applications + invites awaiting their acceptance).</summary>
    Task<IReadOnlyList<GuildJoinRequest>> GetPendingForPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>The single pending request for (guild, player, kind), or null. Used for idempotency.</summary>
    Task<GuildJoinRequest?> FindPendingAsync(Guid guildId, Guid playerId, GuildJoinRequestKind kind, CancellationToken ct = default);

    Task<GuildJoinRequest> CreateAsync(GuildJoinRequest request, CancellationToken ct = default);
    Task UpdateAsync(GuildJoinRequest request, CancellationToken ct = default);
}
