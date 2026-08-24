using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// Admin service: role grant/revoke and punitive moderation (ban/mute) with all safety guards applied.
/// Actor == Guid.Empty is the system/CLI bypass — no DB re-verification.
/// Every punitive action writes to audit_log AND raises a ModerationAction operator email (T40).
/// </summary>
public sealed class AdminService : IAdminService
{
    private readonly IPlayerRepository _players;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditLogRepository _auditLog;
    private readonly IEmailNotificationService _emails;

    public AdminService(
        IPlayerRepository players,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository auditLog,
        IEmailNotificationService emails)
    {
        _players       = players;
        _refreshTokens = refreshTokens;
        _auditLog      = auditLog;
        _emails        = emails;
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

        if (role == PlayerRoles.Player)
            return AdminActionResult.Fail("Cannot revoke the base Player role.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");

        var before = target.Roles;

        // Last-admin protection. Demotion runs atomically (advisory lock + count + clear) so two
        // concurrent demotes of different admins can't both pass the guard and leave zero admins.
        if (role == PlayerRoles.Admin)
        {
            if (!await _players.TryDemoteAdminAsync(target.Id, ct))
                return AdminActionResult.Fail("Cannot demote the last admin.");
            target.RevokeRole(role); // reflect the persisted change on the (detached) entity for audit
        }
        else
        {
            target.RevokeRole(role);
            await _players.UpdateAsync(target, ct);
        }

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

    // Moderation — punitive actions (ban / unban / mute / unmute) — T40

    /// <summary>Validator parity: 30 days, expressed in minutes.</summary>
    private const int MaxMuteMinutes = 30 * 24 * 60;

    /// <inheritdoc/>
    public async Task<AdminActionResult> BanPlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, string? ipAddress = null,
        CancellationToken ct = default)
    {
        // Governance audit 2026-08-22 — northstar §6 grants Moderators "temporary bans up to 3 days"
        // and reserves PERMANENT bans to Admins. Temporary bans do not exist yet (there is no
        // BannedUntil), so every ban this method issues is permanent — which means a Moderator
        // issuing one exceeds the authority §6 grants them. Until temp bans ship, banning is
        // Admin-only; Moderators retain mute. See DESIGN_DECISIONS D-017.
        if (!await ActorIsAdminAsync(actorId, ct))
            return AdminActionResult.Fail(
                "Only an admin may ban. Bans are currently permanent; moderators may mute instead.");

        // Defense in depth behind the validator: §6 forbids reasonless punishment, and the validator
        // is currently the ONLY guard — any future caller (CLI verb, automation) would bypass it.
        if (string.IsNullOrWhiteSpace(reason))
            return AdminActionResult.Fail("A reason is required to ban a player.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        if (target.HasRole(PlayerRoles.Admin))
            return AdminActionResult.Fail("Cannot ban an admin.");
        if (!await ActorMayModerateAsync(actorId, target, ct))
            return AdminActionResult.Fail("Only an admin can moderate staff (moderator/developer) accounts.");

        target.Ban(reason);
        await _players.UpdateAsync(target, ct);
        // A banned player's sessions are killed immediately.
        await _refreshTokens.RevokeAllActiveAsync(target.Id, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "PlayerBanned",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id} reason={reason}",
            ipAddress), ct);

        await QueueModerationEmailAsync(actorId, target, "Ban", reason, expiresAt: null, ipAddress, ct);
        return AdminActionResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<AdminActionResult> UnbanPlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, string? ipAddress = null,
        CancellationToken ct = default)
    {
        // Admin-only, mirroring ban. Governance audit 2026-08-22: before this existed, a ban had NO
        // in-product remedy — reversing a mistaken ban required direct SQL against the players table.
        if (!await ActorIsAdminAsync(actorId, ct))
            return AdminActionResult.Fail("Only an admin may lift a ban.");
        // A reversal is a moderation action too; §6's "every punishment logged with a reason" is
        // worth nothing for disputes if the UNDO is anonymous.
        if (string.IsNullOrWhiteSpace(reason))
            return AdminActionResult.Fail("A reason is required to lift a ban.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        if (!target.IsBanned)
            return AdminActionResult.Fail("Player is not banned.");

        var priorReason = target.BanReason;
        target.Unban();
        await _players.UpdateAsync(target, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "PlayerUnbanned",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id} reason={reason} liftedBanReason={priorReason}",
            ipAddress), ct);

        await QueueModerationEmailAsync(actorId, target, "Unban", reason, expiresAt: null, ipAddress, ct);
        return AdminActionResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<AdminActionResult> MutePlayerAsync(
        Guid actorId, string targetUsernameOrId, int durationMinutes, string reason, string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (durationMinutes <= 0)
            return AdminActionResult.Fail("Mute duration must be a positive number of minutes.");
        // Service-level cap mirroring the validator's 30 days (governance audit 2026-08-22). Without
        // it this method accepts any positive int — a ~4000-year mute — for any non-controller caller.
        if (durationMinutes > MaxMuteMinutes)
            return AdminActionResult.Fail($"Mute duration may not exceed {MaxMuteMinutes / 1440} days.");
        if (string.IsNullOrWhiteSpace(reason))
            return AdminActionResult.Fail("A reason is required to mute a player.");
        if (!await ActorIsModeratorOrAdminAsync(actorId, ct))
            return AdminActionResult.Fail("Actor is not a moderator or admin.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        if (target.HasRole(PlayerRoles.Admin))
            return AdminActionResult.Fail("Cannot mute an admin.");
        if (!await ActorMayModerateAsync(actorId, target, ct))
            return AdminActionResult.Fail("Only an admin can moderate staff (moderator/developer) accounts.");

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(durationMinutes);
        target.Mute(expiresAt);
        await _players.UpdateAsync(target, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "PlayerMuted",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id} minutes={durationMinutes} until={expiresAt:O} reason={reason}",
            ipAddress), ct);

        await QueueModerationEmailAsync(actorId, target, "Mute", reason, expiresAt, ipAddress, ct);
        return AdminActionResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<AdminActionResult> UnmutePlayerAsync(
        Guid actorId, string targetUsernameOrId, string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (!await ActorIsModeratorOrAdminAsync(actorId, ct))
            return AdminActionResult.Fail("Actor is not a moderator or admin.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");

        target.Unmute();
        await _players.UpdateAsync(target, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "PlayerUnmuted",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id}",
            ipAddress), ct);

        await QueueModerationEmailAsync(actorId, target, "Unmute", reason: "Mute lifted", expiresAt: null, ipAddress, ct);
        return AdminActionResult.Ok();
    }

    // Private helpers

    /// <summary>True if the actor may take moderation actions. CLI actor (Guid.Empty) always passes.</summary>
    private async Task<bool> ActorIsModeratorOrAdminAsync(Guid actorId, CancellationToken ct)
    {
        if (actorId == Guid.Empty) return true; // CLI/system bypass
        var actor = await _players.FindByIdAsync(actorId, ct);
        return actor is not null && (actor.HasRole(PlayerRoles.Admin) || actor.HasRole(PlayerRoles.Moderator));
    }

    /// <summary>
    /// True if the actor is an Admin, re-verified against the DB rather than trusting the JWT claim
    /// (a demoted admin's 15-minute access token still carries the old role). CLI actor always passes.
    /// </summary>
    private async Task<bool> ActorIsAdminAsync(Guid actorId, CancellationToken ct)
    {
        if (actorId == Guid.Empty) return true; // CLI/system bypass
        var actor = await _players.FindByIdAsync(actorId, ct);
        return actor is not null && actor.HasRole(PlayerRoles.Admin);
    }

    // Moderation polish (audit ticket): a Moderator may not ban/mute fellow staff — only an Admin
    // (or the CLI/system actor) can act on a Moderator or Developer. Admin targets stay untouchable
    // for everyone (checked separately at each call site). Unmute is intentionally NOT gated — it
    // only removes a restriction.
    private async Task<bool> ActorMayModerateAsync(Guid actorId, Player target, CancellationToken ct)
    {
        if (actorId == Guid.Empty) return true; // CLI/system bypass
        if (!target.HasRole(PlayerRoles.Moderator) && !target.HasRole(PlayerRoles.Developer)) return true;
        var actor = await _players.FindByIdAsync(actorId, ct);
        return actor is not null && actor.HasRole(PlayerRoles.Admin);
    }

    /// <summary>Raises a ModerationAction operator email (T40) for a punitive action. Never blocks.</summary>
    private async Task QueueModerationEmailAsync(
        Guid actorId, Player target, string action, string reason, DateTimeOffset? expiresAt,
        string? ipAddress, CancellationToken ct)
    {
        var detail = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["actorId"] = actorId == Guid.Empty ? null : actorId.ToString(),
            ["targetId"] = target.Id.ToString(),
            ["targetUsername"] = target.Username,
            ["reason"] = reason,
            ["expiresAt"] = expiresAt?.ToString("u"),
        };

        await _emails.QueueAsync(new EmailPayload
        {
            Type = EmailType.ModerationAction,
            Subject = $"{action}: {target.Username}",
            Summary = $"{action} applied to {target.Username} — {reason}",
            TriggeringPlayerId = target.Id,
            TriggeringSystem = "T40",
            Detail = detail,
        }, ipAddress, ct);
    }

    private async Task<Player?> ResolveTargetAsync(string usernameOrId, CancellationToken ct)
    {
        if (Guid.TryParse(usernameOrId, out var guid))
            return await _players.FindByIdAsync(guid, ct);

        return await _players.FindByUsernameAsync(usernameOrId, ct);
    }
}
