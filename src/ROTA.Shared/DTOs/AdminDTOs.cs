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
