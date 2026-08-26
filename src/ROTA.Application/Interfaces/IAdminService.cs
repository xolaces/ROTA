using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Admin operations: role management and beta key tooling.
/// All mutating operations write to audit_log.
/// </summary>
public interface IAdminService
{
    /// <summary>
    /// Grants <paramref name="role"/> to the player identified by <paramref name="targetUsernameOrId"/>.
    /// Resolves by GUID if parseable, otherwise by username.
    /// Re-verifies actor is Admin from DB unless <paramref name="actorId"/> == Guid.Empty (CLI).
    /// </summary>
    Task<AdminActionResult> GrantRoleAsync(
        Guid actorId, string targetUsernameOrId, PlayerRoles role,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes <paramref name="role"/> from the player identified by <paramref name="targetUsernameOrId"/>.
    /// Cannot revoke <c>PlayerRoles.Player</c>.
    /// Cannot revoke <c>Admin</c> from the last remaining admin.
    /// On success, revokes all active refresh tokens for the target.
    /// </summary>
    Task<AdminActionResult> RevokeRoleAsync(
        Guid actorId, string targetUsernameOrId, PlayerRoles role,
        CancellationToken ct = default);

    /// <summary>
    /// Bans the target player (T40). Reason is required; cannot ban an admin. Revokes the target's
    /// sessions, audits, and raises a ModerationAction operator email.
    ///
    /// <para><paramref name="durationDays"/> null = PERMANENT, which northstar §6 reserves to Admins.
    /// A Moderator must supply 1–<see cref="AdminService.MaxModeratorBanDays"/>; anything longer, or
    /// permanent, is refused. This is the split §6 always described — it simply could not be honoured
    /// until BannedUntil existed.</para>
    /// </summary>
    Task<AdminActionResult> BanPlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, int? durationDays = null,
        string? ipAddress = null, CancellationToken ct = default);

    /// <summary>
    /// Lifts a ban (governance audit 2026-08-22). Reason required. A Moderator may lift a TEMPORARY
    /// ban — the class of ban they are allowed to issue — but only an Admin may lift a permanent one.
    /// This is the only in-product remedy for a ban; without it, reversal needs direct SQL.
    /// </summary>
    Task<AdminActionResult> UnbanPlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, string? ipAddress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Mutes the target's chat for <paramref name="durationMinutes"/> (T40). Requires Moderator/Admin;
    /// cannot mute an admin. Audits + raises a ModerationAction email.
    /// </summary>
    Task<AdminActionResult> MutePlayerAsync(
        Guid actorId, string targetUsernameOrId, int durationMinutes, string reason, string? ipAddress = null,
        CancellationToken ct = default);

    /// <summary>
    /// One player's moderation history, newest first, for dispute review (northstar §6). Empty if the
    /// player has none; null only when the player does not exist, which the caller renders as 404.
    /// </summary>
    Task<IReadOnlyList<PunishmentLogEntryResponse>?> GetPunishmentHistoryAsync(
        string targetUsernameOrId, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Lifts an active mute on the target (T40). Audits, writes the §6 governance record, and raises a
    /// ModerationAction email.
    ///
    /// A reason is REQUIRED — a reversal is a moderation action, and §6's "no reasonless punishment"
    /// buys nothing for disputes if the undo is anonymous. A moderator may not lift a mute an admin
    /// placed; that gate reads the provenance out of punishment_log.
    /// </summary>
    Task<AdminActionResult> UnmutePlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, string? ipAddress = null,
        CancellationToken ct = default);
}
