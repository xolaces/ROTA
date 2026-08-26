namespace ROTA.Shared.DTOs;

/// <summary>Result of an admin service operation.</summary>
public class AdminActionResult
{
    public bool Success { get; init; }

    /// <summary>Human-readable failure reason when <see cref="Success"/> is false.</summary>
    public string? FailureReason { get; init; }

    public static AdminActionResult Ok() => new() { Success = true };
    public static AdminActionResult Fail(string reason) => new() { Success = false, FailureReason = reason };
}

/// <summary>Request to grant or revoke a role.</summary>
public class RoleChangeRequest
{
    /// <summary>Role name to grant or revoke (e.g. "Admin", "Moderator").</summary>
    public string Role { get; set; } = string.Empty;
}

/// <summary>Request to ban a player (T40).</summary>
public class BanPlayerRequest
{
    /// <summary>Reason for the ban (recorded + emailed for the dispute trail).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Ban length in whole days. Null = PERMANENT, which northstar §6 reserves to Admins; a Moderator
    /// must supply 1–3. Omitted by older clients, which therefore still mean "permanent" and are still
    /// correctly refused for moderators.
    /// </summary>
    public int? DurationDays { get; set; }
}

/// <summary>Request to lift a ban (governance audit 2026-08-22).</summary>
public class UnbanPlayerRequest
{
    /// <summary>Reason the ban is being lifted (recorded + emailed for the dispute trail).</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// One entry from a player's moderation history, for dispute review (northstar §6).
///
/// Actor role and target username are the values RECORDED AT THE TIME, not resolved now: roles are
/// grantable and revocable and usernames change, so re-resolving either would answer a different
/// question from the one a dispute asks.
/// </summary>
public class PunishmentLogEntryResponse
{
    public long Id { get; set; }

    /// <summary>"Ban" | "Unban" | "Mute" | "Unmute".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Null for the system/CLI actor.</summary>
    public Guid? ActorPlayerId { get; set; }

    /// <summary>The actor's authority at the time: "Admin", "Moderator", or "System".</summary>
    public string ActorRole { get; set; } = string.Empty;

    public string TargetUsername { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>When the punishment lapses. Null means permanent for a ban, n/a for a reversal.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>For a reversal, the id of the entry it lifted. Null if the original predates the log.</summary>
    public long? ReversalOfId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Request to lift a player's mute. A reason is required, as it is for a ban lift.</summary>
public class UnmutePlayerRequest
{
    /// <summary>Reason the mute is being lifted (recorded + emailed for the dispute trail).</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Request to mute a player's chat for a fixed duration (T40).</summary>
public class MutePlayerRequest
{
    /// <summary>Mute duration in minutes (must be positive).</summary>
    public int DurationMinutes { get; set; } = 60;

    /// <summary>Reason for the mute (recorded + emailed).</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Request to generate beta keys.</summary>
public class GenerateBetaKeysRequest
{
    /// <summary>Number of keys to generate (1–100).</summary>
    public int Count { get; set; } = 1;
}

/// <summary>Response listing generated beta keys.</summary>
public class GenerateBetaKeysResponse
{
    public IReadOnlyList<string> Keys { get; init; } = Array.Empty<string>();
}

/// <summary>A beta key with its redemption status for the admin list endpoint.</summary>
public class BetaKeyDto
{
    public string Key { get; init; } = string.Empty;
    public bool IsRedeemed { get; init; }
    public Guid? RedeemedByPlayerId { get; init; }
    public DateTimeOffset? RedeemedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Response from POST /api/admin/leaderboards/stat/refresh.</summary>
public class StatBoardRefreshResponse
{
    /// <summary>Number of eligible players whose Stat board rows were upserted.</summary>
    public int PlayersSnapshotted { get; init; }

    /// <summary>UTC timestamp when the snapshot was taken.</summary>
    public DateTimeOffset SnapshotAt { get; init; }
}
