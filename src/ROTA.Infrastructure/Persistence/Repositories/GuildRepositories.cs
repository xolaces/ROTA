using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class GuildRepository : IGuildRepository
{
    private readonly RotaDbContext _db;
    private readonly string _devGuildTagNormalized;

    public GuildRepository(RotaDbContext db, IOptions<GuildConfig> guildConfig)
    {
        _db = db;
        _devGuildTagNormalized = Guild.Normalize(guildConfig.Value.DevGuildTag);
    }

    public Task<Guild?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Guilds.FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted, ct);

    public Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var n = Guild.Normalize(name);
        return _db.Guilds.FirstOrDefaultAsync(g => g.NameNormalized == n && !g.IsDeleted, ct);
    }

    public Task<Guild?> FindByTagAsync(string tag, CancellationToken ct = default)
    {
        var t = Guild.Normalize(tag);
        return _db.Guilds.FirstOrDefaultAsync(g => g.TagNormalized == t && !g.IsDeleted, ct);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludeGuildId = null, CancellationToken ct = default)
    {
        var n = Guild.Normalize(name);
        return _db.Guilds.AnyAsync(g => g.NameNormalized == n && !g.IsDeleted
            && (excludeGuildId == null || g.Id != excludeGuildId), ct);
    }

    public Task<bool> TagExistsAsync(string tag, Guid? excludeGuildId = null, CancellationToken ct = default)
    {
        var t = Guild.Normalize(tag);
        return _db.Guilds.AnyAsync(g => g.TagNormalized == t && !g.IsDeleted
            && (excludeGuildId == null || g.Id != excludeGuildId), ct);
    }

    public async Task<Guild> CreateAsync(Guild guild, CancellationToken ct = default)
    {
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync(ct);
        return guild;
    }

    public async Task UpdateAsync(Guild guild, CancellationToken ct = default)
    {
        _db.Guilds.Update(guild);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GuildBrowseEntry>> BrowseAsync(
        string? query, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Guilds.AsNoTracking().Where(g => !g.IsDeleted);

        // T43: hide the Dev guild ("The Dev Coffee Shop") from the public browse list. Filtering on the
        // base IQueryable (before Skip/Take) keeps paging correct — the hidden row never counts.
        if (!string.IsNullOrWhiteSpace(_devGuildTagNormalized))
            q = q.Where(g => g.TagNormalized != _devGuildTagNormalized);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = Guild.Normalize(query);
            q = q.Where(g => g.NameNormalized.Contains(needle) || g.TagNormalized.Contains(needle));
        }

        var offset = (page - 1) * pageSize;

        // Join leader for display name; ordered by member count desc (most active first), then name.
        var rows = await q
            .OrderByDescending(g => g.MemberCount)
            .ThenBy(g => g.NameNormalized)
            .Skip(offset)
            .Take(pageSize)
            .Join(_db.Players.AsNoTracking(),
                g => g.LeaderId,
                p => p.Id,
                (g, p) => new GuildBrowseEntry { Guild = g, LeaderDisplayName = p.DisplayName })
            .ToListAsync(ct);

        return rows;
    }

    public async Task<IReadOnlyList<GuildRosterEntry>> GetRosterAsync(Guid guildId, CancellationToken ct = default)
    {
        return await _db.GuildMemberships.AsNoTracking()
            .Where(m => m.GuildId == guildId && !m.IsDeleted)
            .Join(_db.Players.AsNoTracking(),
                m => m.PlayerId,
                p => p.Id,
                (m, p) => new GuildRosterEntry
                {
                    PlayerId = p.Id,
                    Username = p.Username,
                    DisplayName = p.DisplayName,
                    Level = p.Level,
                    Rank = m.Rank,
                    ContributionTotal = m.ContributionTotal,
                    JoinedAt = m.JoinedAt,
                    LastActiveAt = m.LastActiveAt,
                })
            .OrderByDescending(r => r.Rank)
            .ThenBy(r => r.JoinedAt)
            .ToListAsync(ct);
    }
}

public sealed class GuildMembershipRepository : IGuildMembershipRepository
{
    private readonly RotaDbContext _db;
    public GuildMembershipRepository(RotaDbContext db) => _db = db;

    public Task<GuildMembership?> FindByPlayerAsync(Guid playerId, CancellationToken ct = default)
        => _db.GuildMemberships.FirstOrDefaultAsync(m => m.PlayerId == playerId && !m.IsDeleted, ct);

    public Task<GuildMembership?> FindByGuildAndPlayerAsync(Guid guildId, Guid playerId, CancellationToken ct = default)
        => _db.GuildMemberships.FirstOrDefaultAsync(
            m => m.GuildId == guildId && m.PlayerId == playerId && !m.IsDeleted, ct);

    public async Task<IReadOnlyList<GuildMembership>> GetForGuildAsync(Guid guildId, CancellationToken ct = default)
        => await _db.GuildMemberships.AsNoTracking()
            .Where(m => m.GuildId == guildId && !m.IsDeleted)
            .ToListAsync(ct);

    public Task<int> CountActiveAsync(Guid guildId, CancellationToken ct = default)
        => _db.GuildMemberships.CountAsync(m => m.GuildId == guildId && !m.IsDeleted, ct);

    public async Task<GuildMembership> CreateAsync(GuildMembership membership, CancellationToken ct = default)
    {
        _db.GuildMemberships.Add(membership);
        await _db.SaveChangesAsync(ct);
        return membership;
    }

    public async Task UpdateAsync(GuildMembership membership, CancellationToken ct = default)
    {
        _db.GuildMemberships.Update(membership);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class GuildJoinRequestRepository : IGuildJoinRequestRepository
{
    private readonly RotaDbContext _db;
    public GuildJoinRequestRepository(RotaDbContext db) => _db = db;

    public Task<GuildJoinRequest?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.GuildJoinRequests.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);

    public async Task<IReadOnlyList<GuildJoinRequest>> GetPendingForGuildAsync(Guid guildId, CancellationToken ct = default)
        => await _db.GuildJoinRequests.AsNoTracking()
            .Where(r => r.GuildId == guildId && r.Status == GuildJoinRequestStatus.Pending && !r.IsDeleted)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    // Officer review queue with WHO is applying — mirrors GetRosterAsync's join projection. Without
    // the join the DTO carried only a PlayerId and both clients rendered a raw GUID as the applicant.
    public async Task<IReadOnlyList<GuildJoinRequestEntry>> GetPendingForGuildWithPlayersAsync(Guid guildId, CancellationToken ct = default)
        => await _db.GuildJoinRequests.AsNoTracking()
            .Where(r => r.GuildId == guildId && r.Status == GuildJoinRequestStatus.Pending && !r.IsDeleted)
            .Join(_db.Players.AsNoTracking(),
                r => r.PlayerId,
                p => p.Id,
                (r, p) => new GuildJoinRequestEntry
                {
                    Id = r.Id,
                    GuildId = r.GuildId,
                    PlayerId = r.PlayerId,
                    Username = p.Username,
                    DisplayName = p.DisplayName,
                    Level = p.Level,
                    Kind = r.Kind,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt,
                })
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GuildJoinRequest>> GetPendingForPlayerAsync(Guid playerId, CancellationToken ct = default)
        => await _db.GuildJoinRequests.AsNoTracking()
            .Where(r => r.PlayerId == playerId && r.Status == GuildJoinRequestStatus.Pending && !r.IsDeleted)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<GuildJoinRequest?> FindPendingAsync(Guid guildId, Guid playerId, GuildJoinRequestKind kind, CancellationToken ct = default)
        => _db.GuildJoinRequests.FirstOrDefaultAsync(
            r => r.GuildId == guildId && r.PlayerId == playerId && r.Kind == kind
                && r.Status == GuildJoinRequestStatus.Pending && !r.IsDeleted, ct);

    public async Task<GuildJoinRequest> CreateAsync(GuildJoinRequest request, CancellationToken ct = default)
    {
        _db.GuildJoinRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task UpdateAsync(GuildJoinRequest request, CancellationToken ct = default)
    {
        _db.GuildJoinRequests.Update(request);
        await _db.SaveChangesAsync(ct);
    }
}
