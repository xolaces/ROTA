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
}
