using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>
/// A roster row: a membership joined to its player for display (System 21 Slice 1).
/// </summary>
public sealed class GuildRosterEntry
{
    public Guid PlayerId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int Level { get; init; }
    public ROTA.Domain.Enums.GuildRank Rank { get; init; }
    public long ContributionTotal { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset LastActiveAt { get; init; }
}

/// <summary>
/// A guild row for the browse list, with its leader's display name (System 21 Slice 1).
/// </summary>
public sealed class GuildBrowseEntry
{
    public Guild Guild { get; init; } = null!;
    public string LeaderDisplayName { get; init; } = string.Empty;
}

public interface IGuildRepository
{
    Task<Guild?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Find an active guild whose name matches case-insensitively (uses the normalized column).</summary>
    Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default);

    /// <summary>Find an active guild whose tag matches case-insensitively (uses the normalized column).</summary>
    Task<Guild?> FindByTagAsync(string tag, CancellationToken ct = default);

    /// <summary>True if an active guild already uses this name (case-insensitive). Optionally excludes one id.</summary>
    Task<bool> NameExistsAsync(string name, Guid? excludeGuildId = null, CancellationToken ct = default);

    /// <summary>True if an active guild already uses this tag (case-insensitive). Optionally excludes one id.</summary>
    Task<bool> TagExistsAsync(string tag, Guid? excludeGuildId = null, CancellationToken ct = default);

    Task<Guild> CreateAsync(Guild guild, CancellationToken ct = default);
    Task UpdateAsync(Guild guild, CancellationToken ct = default);

    /// <summary>Searchable, paged browse over active guilds. <paramref name="query"/> matches name/tag.</summary>
    Task<IReadOnlyList<GuildBrowseEntry>> BrowseAsync(string? query, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Active memberships of a guild, joined to players for display, ordered by rank desc then join date.</summary>
    Task<IReadOnlyList<GuildRosterEntry>> GetRosterAsync(Guid guildId, CancellationToken ct = default);
}
