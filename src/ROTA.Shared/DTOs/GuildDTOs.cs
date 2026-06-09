namespace ROTA.Shared.DTOs;

// ── Guild (System 21 Slice 1) DTOs ───────────────────────────────────────────

/// <summary>Failure reason for a guild operation. Controllers map to HTTP codes.</summary>
public enum GuildFailureCode
{
    None = 0,
    NotFound = 1,            // 404 — guild / request / target missing
    Validation = 2,          // 400 — bad input the validator didn't catch (length/reserved)
    AlreadyInGuild = 3,      // 409 — actor or target already in a guild
    NotInGuild = 4,          // 409 — actor/target not a member where required
    NameTaken = 5,           // 409 — name in use (case-insensitive)
    TagTaken = 6,            // 409 — tag in use (case-insensitive)
    InsufficientLevel = 7,   // 409 — below MinCreationLevel
    InsufficientGold = 8,    // 409 — below CreationGoldCost
    MemberCapReached = 9,    // 409 — at MemberCap
    PermissionDenied = 10,   // 403 — actor rank too low for the action
    PolicyForbidsApply = 11, // 409 — InviteOnly guild can't be applied to
    LeaderCannotLeave = 12,  // 409 — leader must transfer/disband first
    Conflict = 13,           // 409 — generic conflict (e.g. duplicate insert lost the race)
    DevGuildRestricted = 14, // 403 — dev-guild gate (devs locked to it; non-devs barred from it) (T43)
}

/// <summary>Generic result of a guild command (no payload).</summary>
public class GuildActionResult
{
    public bool Success { get; init; }
    public GuildFailureCode FailureCode { get; init; }
    public string? FailureReason { get; init; }

    public static GuildActionResult Ok() => new() { Success = true };
    public static GuildActionResult Fail(GuildFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}

/// <summary>Result of creating a guild — carries the new guild id on success.</summary>
public class CreateGuildResult
{
    public bool Success { get; init; }
    public GuildFailureCode FailureCode { get; init; }
    public string? FailureReason { get; init; }
    public Guid? GuildId { get; init; }

    public static CreateGuildResult Ok(Guid guildId) => new() { Success = true, GuildId = guildId };
    public static CreateGuildResult Fail(GuildFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}

/// <summary>Result of applying to a guild — distinguishes auto-join from a pending application.</summary>
public class ApplyGuildResult
{
    public bool Success { get; init; }
    public GuildFailureCode FailureCode { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>True when an Open guild auto-accepted (player is now a member).</summary>
    public bool Joined { get; init; }

    /// <summary>The pending application's id when the guild requires approval; null on auto-join.</summary>
    public Guid? RequestId { get; init; }

    public static ApplyGuildResult JoinedNow() => new() { Success = true, Joined = true };
    public static ApplyGuildResult Pending(Guid requestId)
        => new() { Success = true, Joined = false, RequestId = requestId };
    public static ApplyGuildResult Fail(GuildFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}

// ── Requests ─────────────────────────────────────────────────────────────────

public class CreateGuildRequest
{
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Open / Application / InviteOnly. Defaults to Application when omitted.</summary>
    public string JoinPolicy { get; set; } = "Application";
}

public class GuildInviteRequest
{
    /// <summary>Target player username or GUID.</summary>
    public string Target { get; set; } = string.Empty;
}

public class UpdateGuildRequest
{
    public string? Name { get; set; }
    public string? Tag { get; set; }
    public string? Description { get; set; }
    public string? Motd { get; set; }
    /// <summary>Open / Application / InviteOnly. Null = unchanged.</summary>
    public string? JoinPolicy { get; set; }
}

public class TransferLeadershipRequest
{
    /// <summary>Target member player GUID.</summary>
    public Guid TargetPlayerId { get; set; }
}

// ── Responses ────────────────────────────────────────────────────────────────

public class GuildSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string JoinPolicy { get; init; } = string.Empty;
    public int Level { get; init; }
    public int MemberCount { get; init; }
    public int MemberCap { get; init; }
    public string LeaderDisplayName { get; init; } = string.Empty;
}

public class GuildMemberDto
{
    public Guid PlayerId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int Level { get; init; }
    public string Rank { get; init; } = string.Empty;
    public long ContributionTotal { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset LastActiveAt { get; init; }
}

public class GuildJoinRequestDto
{
    public Guid Id { get; init; }
    public Guid GuildId { get; init; }
    public Guid PlayerId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Full guild detail with roster (and pending requests when the caller is officer+).</summary>
public class GuildDetailResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Motd { get; init; } = string.Empty;
    public string? CrestId { get; init; }
    public string JoinPolicy { get; init; } = string.Empty;
    public int Level { get; init; }
    public long Xp { get; init; }
    public int MemberCount { get; init; }
    public int MemberCap { get; init; }
    public Guid LeaderId { get; init; }

    /// <summary>The caller's rank in this guild, or null if not a member.</summary>
    public string? CallerRank { get; init; }

    public IReadOnlyList<GuildMemberDto> Members { get; init; } = Array.Empty<GuildMemberDto>();

    /// <summary>Pending join requests — populated only when the caller is an officer+.</summary>
    public IReadOnlyList<GuildJoinRequestDto> PendingRequests { get; init; } = Array.Empty<GuildJoinRequestDto>();
}
