namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// A guild / clan — the persistent social container (System 21 Slice 1). One leader, up to
/// <see cref="MemberCap"/> members, a per-guild <see cref="JoinPolicy"/>. Name and Tag are unique
/// case-insensitively, enforced by lowercase shadow columns (<see cref="NameNormalized"/>,
/// <see cref="TagNormalized"/>) under unique indexes — kept in sync by the mutating methods so the
/// service/repo can query and the DB can enforce uniqueness without a functional index.
/// </summary>
public class Guild
{
    // Required by EF Core
    private Guild() { }

    /// <summary>
    /// Founds a guild with the creator as Leader and a denormalized member count of 1.
    /// Caller is responsible for level/gold gates, name/tag validation, and creating the
    /// Leader <see cref="GuildMembership"/> row.
    /// </summary>
    public static Guild Create(
        Guid leaderId,
        string name,
        string tag,
        string description,
        GuildJoinPolicy joinPolicy,
        int memberCap)
    {
        var now = DateTimeOffset.UtcNow;
        return new Guild
        {
            Id = Guid.NewGuid(),
            Name = name,
            NameNormalized = Normalize(name),
            Tag = tag,
            TagNormalized = Normalize(tag),
            Description = description ?? string.Empty,
            CrestId = null,
            LeaderId = leaderId,
            Motd = string.Empty,
            MemberCap = memberCap,
            Level = 1,
            Xp = 0,
            MemberCount = 1,
            JoinPolicy = joinPolicy,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        };
    }

    public Guid Id { get; private set; }

    /// <summary>Display name (case preserved). Unique case-insensitively via <see cref="NameNormalized"/>.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Lowercase shadow of <see cref="Name"/> — the unique-index target. Never shown.</summary>
    public string NameNormalized { get; private set; } = string.Empty;

    /// <summary>Short tag/abbreviation (2–5 chars, case preserved). Unique case-insensitively.</summary>
    public string Tag { get; private set; } = string.Empty;

    /// <summary>Lowercase shadow of <see cref="Tag"/> — the unique-index target. Never shown.</summary>
    public string TagNormalized { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Crest/emblem identifier (cosmetic). Nullable — set in a later slice.</summary>
    public string? CrestId { get; private set; }

    /// <summary>Current leader's player id (FK, indexed).</summary>
    public Guid LeaderId { get; private set; }

    /// <summary>Message of the day (officer+ editable).</summary>
    public string Motd { get; private set; } = string.Empty;

    public int MemberCap { get; private set; }
    public int Level { get; private set; } = 1;
    public long Xp { get; private set; }

    /// <summary>Denormalized active-member count for O(1) reads. Kept in sync by the service.</summary>
    public int MemberCount { get; private set; }

    public GuildJoinPolicy JoinPolicy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; }

    // ── Domain methods ────────────────────────────────────────────────────────

    public void Rename(string name)
    {
        Name = name;
        NameNormalized = Normalize(name);
        Touch();
    }

    public void SetTag(string tag)
    {
        Tag = tag;
        TagNormalized = Normalize(tag);
        Touch();
    }

    public void SetDescription(string description)
    {
        Description = description ?? string.Empty;
        Touch();
    }

    public void SetMotd(string motd)
    {
        Motd = motd ?? string.Empty;
        Touch();
    }

    public void SetCrest(string? crestId)
    {
        CrestId = crestId;
        Touch();
    }

    public void SetJoinPolicy(GuildJoinPolicy policy)
    {
        JoinPolicy = policy;
        Touch();
    }

    public void SetLeader(Guid playerId)
    {
        LeaderId = playerId;
        Touch();
    }

    public void AddXp(long amount)
    {
        if (amount <= 0) return;
        Xp += amount;
        Touch();
    }

    public void IncrementMemberCount()
    {
        MemberCount++;
        Touch();
    }

    public void DecrementMemberCount()
    {
        if (MemberCount > 0) MemberCount--;
        Touch();
    }

    public void Disband()
    {
        IsDeleted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    /// <summary>Case-folding used for uniqueness. Invariant lower + trim — matches the validator/service.</summary>
    public static string Normalize(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
