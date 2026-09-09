namespace ROTA.Domain.Enums;

/// <summary>
/// Classification for an operator notification (the "outbound email"). Drives the subject-line tag,
/// body metadata, and the dashboard's per-type colour/grouping. Designed so new types are a one-line
/// addition here plus a producer that builds the payload — nothing else needs to change.
/// </summary>
public enum EmailType
{
    /// <summary>Beta in-game bug report.</summary>
    BugReport = 1,

    /// <summary>Player-filed report against another player.</summary>
    PlayerReport = 2,

    /// <summary>Punitive action by a moderator/admin: ban, mute, role change.</summary>
    ModerationAction = 3,

    /// <summary>First player to reach a pinnacle level post-launch.</summary>
    PinnacleFirstClaim = 4,

    /// <summary>General in-game feedback / ticket.</summary>
    GeneralTicket = 5,

    /// <summary>Password-reset code sent to the PLAYER (not the operator) — T65.</summary>
    PasswordReset = 6,
}
