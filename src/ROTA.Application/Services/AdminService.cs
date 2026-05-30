using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// Admin service: role grant/revoke with all safety guards applied.
/// Actor == Guid.Empty is the system/CLI bypass — no DB re-verification.
/// </summary>
public sealed class AdminService : IAdminService
{
    private readonly IPlayerRepository _players;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditLogRepository _auditLog;

    public AdminService(
        IPlayerRepository players,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository auditLog)
    {
        _players       = players;
        _refreshTokens = refreshTokens;
        _auditLog      = auditLog;
    }

    /// <inheritdoc/>
    public async Task<AdminActionResult> GrantRoleAsync(
        Guid actorId, string targetUsernameOrId, PlayerRoles role,
        CancellationToken ct = default)
    {
        // Re-verify actor from DB (skip for CLI actor Guid.Empty).
        if (actorId != Guid.Empty)
        {
            var actor = await _players.FindByIdAsync(actorId, ct);
            if (actor is null || !actor.HasRole(PlayerRoles.Admin))
                return AdminActionResult.Fail("Actor is not an admin.");
        }

        // Cannot grant the base Player flag (it is always set; granting is a no-op but guard anyway).
        if (role == PlayerRoles.Player)
            return AdminActionResult.Fail("Cannot explicitly grant the base Player role.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");

        var before = target.Roles;
        target.GrantRole(role);
        await _players.UpdateAsync(target, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "RoleGranted",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id} role={role} before={before} after={target.Roles}",
            ipAddress: null));

        return AdminActionResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<AdminActionResult> RevokeRoleAsync(
        Guid actorId, string targetUsernameOrId, PlayerRoles role,
        CancellationToken ct = default)
    {
        // Re-verify actor from DB (skip for CLI actor Guid.Empty).
        if (actorId != Guid.Empty)
        {
            var actor = await _players.FindByIdAsync(actorId, ct);
            if (actor is null || !actor.HasRole(PlayerRoles.Admin))
                return AdminActionResult.Fail("Actor is not an admin.");
        }

        // Cannot revoke the base Player role.
        if (role == PlayerRoles.Player)
            return AdminActionResult.Fail("Cannot revoke the base Player role.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");

        // Guard: last-admin protection.
        if (role == PlayerRoles.Admin)
        {
            var adminCount = await _players.CountByRoleAsync(PlayerRoles.Admin, ct);
            if (adminCount <= 1)
                return AdminActionResult.Fail("Cannot demote the last admin.");
        }

        var before = target.Roles;
        target.RevokeRole(role);
        await _players.UpdateAsync(target, ct);

        // Revoke active sessions so the removed privilege is effective immediately.
        if (role is PlayerRoles.Admin or PlayerRoles.Moderator)
            await _refreshTokens.RevokeAllActiveAsync(target.Id, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "RoleRevoked",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id} role={role} before={before} after={target.Roles}",
            ipAddress: null));

        return AdminActionResult.Ok();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task<Player?> ResolveTargetAsync(string usernameOrId, CancellationToken ct)
    {
        if (Guid.TryParse(usernameOrId, out var guid))
            return await _players.FindByIdAsync(guid, ct);

        return await _players.FindByUsernameAsync(usernameOrId, ct);
    }
}
