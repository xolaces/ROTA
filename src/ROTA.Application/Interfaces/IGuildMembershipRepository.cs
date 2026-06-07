using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IGuildMembershipRepository
{
    /// <summary>The player's single ACTIVE membership, or null if guild-less.</summary>
    Task<GuildMembership?> FindByPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>The active membership of a specific player in a specific guild, or null.</summary>
    Task<GuildMembership?> FindByGuildAndPlayerAsync(Guid guildId, Guid playerId, CancellationToken ct = default);

    /// <summary>All active memberships for a guild (entities, not display-joined).</summary>
    Task<IReadOnlyList<GuildMembership>> GetForGuildAsync(Guid guildId, CancellationToken ct = default);

    /// <summary>Count of active members in a guild (authoritative cap check).</summary>
    Task<int> CountActiveAsync(Guid guildId, CancellationToken ct = default);

    Task<GuildMembership> CreateAsync(GuildMembership membership, CancellationToken ct = default);
    Task UpdateAsync(GuildMembership membership, CancellationToken ct = default);
}
