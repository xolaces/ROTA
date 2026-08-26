using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

/// <summary>
/// One moderation action against one player. Append-only.
///
/// Northstar §6, binding: "Every punishment, by any role, against any player, is logged -- actor,
/// role, target, type, reason, duration/expiry, timestamp. Append-only, like the audit log."
///
/// Until this existed those facts survived only as free text inside <c>audit_log.result_summary</c>,
/// with no actor-role snapshot and no expiry at all, which made dispute review impractical: you could
/// not answer "who placed this, under what authority, and when does it end?" without parsing prose.
///
/// Two fields carry more weight than they look:
///
///   ACTOR ROLE IS A SNAPSHOT, not a join. Roles are grantable and revocable, so reading the actor's
///   role at review time answers a different question from the one a dispute asks -- a moderator who
///   was later promoted or demoted would appear to have acted with authority they did not have.
///
///   TARGET USERNAME IS A SNAPSHOT for the same reason: usernames change, and a punishment record
///   that silently renames itself is not a record.
/// </summary>
public class PunishmentLog
{
    /// <summary>A punishment being applied. <paramref name="expiresAt"/> null means permanent.</summary>
    public static PunishmentLog Create(
        Guid? actorPlayerId,
        string actorRole,
        Guid targetPlayerId,
        string targetUsername,
        PunishmentType type,
        string reason,
        DateTimeOffset? expiresAt,
        long? reversalOfId,
        string? ipAddress) => new PunishmentLog
        {
            ActorPlayerId  = actorPlayerId,
            ActorRole      = actorRole,
            TargetPlayerId = targetPlayerId,
            TargetUsername = targetUsername,
            Type           = type,
            Reason         = reason,
            ExpiresAt      = expiresAt,
            ReversalOfId   = reversalOfId,
            IpAddress      = ipAddress,
            CreatedAt      = DateTimeOffset.UtcNow,
        };

    public long Id { get; private set; }

    /// <summary>Null for the system/CLI actor (<c>Guid.Empty</c>), which has no player row.</summary>
    public Guid? ActorPlayerId { get; private set; }

    /// <summary>
    /// The actor's authority AT THE TIME OF THE ACTION -- "Admin", "Moderator", or "System" for the
    /// CLI. Snapshotted deliberately; see the type remarks.
    /// </summary>
    public string ActorRole { get; private set; } = string.Empty;

    public Guid TargetPlayerId { get; private set; }

    /// <summary>The target's username at the time of the action.</summary>
    public string TargetUsername { get; private set; } = string.Empty;

    public PunishmentType Type { get; private set; }

    /// <summary>Never null or blank: §6 forbids reasonless punishment, reversals included.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// When the punishment lapses. Null means permanent for a ban, and is simply not applicable to a
    /// reversal.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>
    /// For a reversal, the entry being reversed. This is what makes "who placed the mute I am about to
    /// lift, and with what authority?" answerable, which the §6 reversal gates depend on.
    /// </summary>
    public long? ReversalOfId { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
