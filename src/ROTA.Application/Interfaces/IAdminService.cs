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
    /// Bans the target player (T40). **Admin-only** — bans are permanent until temporary bans exist,
    /// and northstar §6 reserves permanent bans to Admins (governance audit 2026-08-22). Reason is
    /// required. Cannot ban an admin. Revokes the target's sessions, audits, and raises a
    /// ModerationAction operator email.
    /// </summary>
    Task<AdminActionResult> BanPlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, string? ipAddress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lifts a ban (governance audit 2026-08-22). Admin-only, reason required. This is the only
    /// in-product remedy for a ban — without it, reversal needs direct SQL.
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

    /// <summary>Lifts any active mute on the target (T40). Audits + raises a ModerationAction email.</summary>
    Task<AdminActionResult> UnmutePlayerAsync(
        Guid actorId, string targetUsernameOrId, string? ipAddress = null,
        CancellationToken ct = default);
}
