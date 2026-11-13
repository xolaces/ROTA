namespace ROTA.Domain.Enums;

/// <summary>
/// Why a refresh token stopped working. Stored so a later presentation of the token can be judged.
///
/// Only <see cref="Rotated"/> makes a second use suspicious: the legitimate client already holds the
/// replacement, so whoever presents the old one has a copy, and the whole family is revoked. Every other
/// reason ended the session on purpose (logout, the session cap, a password reset, a moderator action),
/// and a stale client presenting that token again is an ordinary failed refresh, not a theft signal.
/// Treating them all as theft logged players out of every device when an old one woke up.
/// </summary>
public enum RefreshTokenRevokeReason
{
    /// <summary>Consumed by a refresh; the client was handed a new pair.</summary>
    Rotated        = 1,
    /// <summary>The player logged out of this session.</summary>
    LoggedOut      = 2,
    /// <summary>Pushed out as the oldest session when a new login hit the concurrent-session cap.</summary>
    SessionLimit   = 3,
    /// <summary>Every session ends when the password is reset.</summary>
    PasswordReset  = 4,
    /// <summary>Revoked because a rotated token from the same family was replayed.</summary>
    ReplayDetected = 5,
    /// <summary>An Admin or Moderator role was removed, so the old claims must not live on.</summary>
    RoleRevoked    = 6,
    /// <summary>The player was banned.</summary>
    Banned         = 7,
    /// <summary>The player changed their password while logged in; every session is ended and the
    /// device that made the change gets a new one.</summary>
    PasswordChanged = 8,
    /// <summary>The player chose "log out everywhere".</summary>
    LoggedOutEverywhere = 9,
}
