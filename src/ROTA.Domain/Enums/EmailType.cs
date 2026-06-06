namespace ROTA.Domain.Enums;

/// <summary>
/// Classification for an operator notification (the "outbound email"). Drives the subject-line tag,
/// body metadata, and the dashboard's per-type colour/grouping. Designed so new types are a one-line
/// addition here plus a producer that builds the payload — nothing else needs to change.
/// </summary>
public enum EmailType
{
    /// <summary>Beta in-game bug report (T38).</summary>
    BugReport = 1,

    /// <summary>Player-filed report against another player (T37).</summary>
    PlayerReport = 2,

    /// <summary>Punitive action by a moderator/admin: ban, mute, role change (T40).</summary>
    ModerationAction = 3,

    /// <summary>First player to reach a pinnacle level post-launch (T33).</summary>
    PinnacleFirstClaim = 4,

    /// <summary>General in-game feedback / ticket (T38).</summary>
    GeneralTicket = 5,
}
