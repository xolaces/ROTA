using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// Admin service: role grant/revoke and punitive moderation (ban/mute) with all safety guards applied.
/// Actor == Guid.Empty is the system/CLI bypass — no DB re-verification.
///
/// Every punitive action writes THREE records, and they are not redundant:
///   audit_log      — the operational trail, uniform across every action in the system.
///   punishment_log — the governance record northstar §6 requires (actor, ROLE, target, type, reason,
///                    expiry, timestamp), structured so a dispute can be reviewed without parsing prose.
///   ModerationAction email — the operator notification (T40).
/// </summary>
public sealed class AdminService : IAdminService
{
    private readonly IPlayerRepository _players;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditLogRepository _auditLog;
    private readonly IPunishmentLogRepository _punishments;
    private readonly IEmailNotificationService _emails;

    public AdminService(
        IPlayerRepository players,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository auditLog,
        IPunishmentLogRepository punishments,
        IEmailNotificationService emails)
    {
        _players       = players;
        _refreshTokens = refreshTokens;
        _auditLog      = auditLog;
        _punishments   = punishments;
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
    /// <summary>Northstar §6: a Moderator's ban is capped at three days.</summary>
    public const int MaxModeratorBanDays = 3;

    /// <summary>Ceiling for ANY dated ban. Beyond a decade, the honest word is permanent.</summary>
    public const int MaxBanDays = 3650;

    private const int MaxMuteMinutes = 30 * 24 * 60;

    /// <inheritdoc/>
    public async Task<AdminActionResult> BanPlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, int? durationDays = null,
        string? ipAddress = null, CancellationToken ct = default)
    {
        if (!await ActorIsModeratorOrAdminAsync(actorId, ct))
            return AdminActionResult.Fail("Actor is not a moderator or admin.");

        // Defense in depth behind the validator: §6 forbids reasonless punishment, and the validator
        // is only reached through the controller — a CLI verb or automation would bypass it.
        if (string.IsNullOrWhiteSpace(reason))
            return AdminActionResult.Fail("A reason is required to ban a player.");
        if (durationDays is <= 0)
            return AdminActionResult.Fail("A ban duration must be a positive number of days.");
        if (durationDays > MaxBanDays)
            return AdminActionResult.Fail(
                $"A ban may not exceed {MaxBanDays} days — omit the duration for a permanent ban.");

        // Northstar §6: Moderators get "temporary bans up to 3 days"; PERMANENT bans are the Admin's.
        // This is the split §6 always described — it simply could not be honoured until BannedUntil
        // existed, which is why banning was Admin-only in the interim (D-017).
        bool actorIsAdmin = await ActorIsAdminAsync(actorId, ct);
        if (!actorIsAdmin)
        {
            if (durationDays is null)
                return AdminActionResult.Fail(
                    "Only an admin may issue a permanent ban. Set a duration of "
                    + $"{MaxModeratorBanDays} days or fewer.");
            if (durationDays > MaxModeratorBanDays)
                return AdminActionResult.Fail(
                    $"A moderator may not ban for more than {MaxModeratorBanDays} days.");
        }

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        if (target.HasRole(PlayerRoles.Admin))
            return AdminActionResult.Fail("Cannot ban an admin.");
        if (!await ActorMayModerateAsync(actorId, target, ct))
            return AdminActionResult.Fail("Only an admin can moderate staff (moderator/developer) accounts.");

        var until = durationDays.HasValue
            ? DateTimeOffset.UtcNow.AddDays(durationDays.Value)
            : (DateTimeOffset?)null;

        target.Ban(reason, until);
        await _players.UpdateAsync(target, ct);
        // A banned player's sessions are killed immediately. Expiry does NOT restore them — the player
        // simply signs in again, which is the correct outcome either way.
        await _refreshTokens.RevokeAllActiveAsync(target.Id, ct);

        var durationLabel = durationDays.HasValue ? durationDays.Value + "d" : "permanent";
        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "PlayerBanned",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id} reason={reason} "
                         + $"duration={durationLabel} until={until?.ToString("O") ?? "-"}",
            ipAddress), ct);

        await AppendPunishmentAsync(
            actorId, target, PunishmentType.Ban, reason, expiresAt: until, reversalOf: null, ipAddress, ct);
        await QueueModerationEmailAsync(actorId, target, "Ban", reason, expiresAt: until, ipAddress, ct);
        return AdminActionResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<AdminActionResult> UnbanPlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, string? ipAddress = null,
        CancellationToken ct = default)
    {
        // Governance audit 2026-08-22: before this existed, a ban had NO in-product remedy — reversing
        // a mistaken ban required direct SQL against the players table.
        if (!await ActorIsModeratorOrAdminAsync(actorId, ct))
            return AdminActionResult.Fail("Actor is not a moderator or admin.");
        // A reversal is a moderation action too; §6's "every punishment logged with a reason" is
        // worth nothing for disputes if the UNDO is anonymous.
        if (string.IsNullOrWhiteSpace(reason))
            return AdminActionResult.Fail("A reason is required to lift a ban.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        if (!target.IsBanned)
            return AdminActionResult.Fail("Player is not banned.");

        // A moderator may lift a TEMPORARY ban — the class of ban they are allowed to issue — but a
        // permanent ban is the Admin's to place and the Admin's to lift. Without this, the §6 split on
        // issuing would be trivially bypassed from the other direction.
        if (target.BannedUntil is null && !await ActorIsAdminAsync(actorId, ct))
            return AdminActionResult.Fail("Only an admin may lift a permanent ban.");

        // Read BEFORE the reversal is appended, so the lookup cannot find its own Unban row.
        var activeBan = await _punishments.FindActivePunishmentAsync(target.Id, PunishmentType.Ban, ct);

        var priorReason = target.BanReason;
        target.Unban();
        await _players.UpdateAsync(target, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "PlayerUnbanned",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id} reason={reason} liftedBanReason={priorReason}",
            ipAddress), ct);

        await AppendPunishmentAsync(
            actorId, target, PunishmentType.Unban, reason, expiresAt: null, reversalOf: activeBan?.Id,
            ipAddress, ct);
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

        await AppendPunishmentAsync(
            actorId, target, PunishmentType.Mute, reason, expiresAt, reversalOf: null, ipAddress, ct);
        await QueueModerationEmailAsync(actorId, target, "Mute", reason, expiresAt, ipAddress, ct);
        return AdminActionResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<AdminActionResult> UnmutePlayerAsync(
        Guid actorId, string targetUsernameOrId, string reason, string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (!await ActorIsModeratorOrAdminAsync(actorId, ct))
            return AdminActionResult.Fail("Actor is not a moderator or admin.");
        // Governance audit 2026-08-22: the reason used to be the hardcoded literal "Mute lifted", so
        // every reversal in the record was indistinguishable from every other. §6's "no reasonless
        // punishment" is worth nothing if the UNDO is anonymous — the same argument that already
        // applies to Unban.
        if (string.IsNullOrWhiteSpace(reason))
            return AdminActionResult.Fail("A reason is required to lift a mute.");

        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null)
            return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        // Mirrors Unban. Without it a non-muted player yields a phantom reversal in the governance
        // record — an entry describing something that never happened.
        if (!target.IsMuted)
            return AdminActionResult.Fail("Player is not muted.");

        // Read BEFORE appending, so the lookup cannot find its own Unmute row.
        var activeMute = await _punishments.FindActivePunishmentAsync(target.Id, PunishmentType.Mute, ct);

        // Governance audit 2026-08-22: ANY moderator could lift ANY admin-placed mute, silently
        // overriding a decision made with higher authority. This is the mute-side counterpart of the
        // rule Unban already enforces (a moderator may lift a temporary ban, never a permanent one),
        // and it is only answerable now that punishment_log records WHO placed the mute and under what
        // role.
        //
        // A mute with no recorded provenance predates this log. Those stay liftable by a moderator:
        // being strict would leave legacy mutes with no in-product remedy, which is exactly the failure
        // that made unban necessary in the first place. New mutes are all recorded, so the hole closes
        // on its own.
        if (activeMute is not null
            && string.Equals(activeMute.ActorRole, "Admin", StringComparison.Ordinal)
            && !await ActorIsAdminAsync(actorId, ct))
        {
            return AdminActionResult.Fail("Only an admin may lift a mute placed by an admin.");
        }

        target.Unmute();
        await _players.UpdateAsync(target, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId == Guid.Empty ? null : actorId,
            "PlayerUnmuted",
            inputHash: null,
            resultSummary: $"actor={actorId} target={target.Id} reason={reason}",
            ipAddress), ct);

        await AppendPunishmentAsync(
            actorId, target, PunishmentType.Unmute, reason, expiresAt: null, reversalOf: activeMute?.Id,
            ipAddress, ct);
        await QueueModerationEmailAsync(actorId, target, "Unmute", reason, expiresAt: null, ipAddress, ct);
        return AdminActionResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PunishmentLogEntryResponse>?> GetPunishmentHistoryAsync(
        string targetUsernameOrId, int limit = 100, CancellationToken ct = default)
    {
        var target = await ResolveTargetAsync(targetUsernameOrId, ct);
        if (target is null) return null;

        // Clamped rather than trusted: the parameter reaches this from a query string.
        limit = Math.Clamp(limit, 1, 500);

        var entries = await _punishments.GetHistoryAsync(target.Id, limit, ct);
        return entries.Select(e => new PunishmentLogEntryResponse
        {
            Id             = e.Id,
            Type           = e.Type.ToString(),
            ActorPlayerId  = e.ActorPlayerId,
            ActorRole      = e.ActorRole,
            TargetUsername = e.TargetUsername,
            Reason         = e.Reason,
            ExpiresAt      = e.ExpiresAt,
            ReversalOfId   = e.ReversalOfId,
            CreatedAt      = e.CreatedAt,
        }).ToList();
    }

    // Private helpers

    /// <summary>
    /// Writes the northstar §6 governance record. Separate from the audit trail on purpose: audit_log
    /// is uniform free text across every action in the system, which is fine for operations and
    /// useless for dispute review.
    /// </summary>
    private async Task AppendPunishmentAsync(
        Guid actorId, Player target, PunishmentType type, string reason,
        DateTimeOffset? expiresAt, long? reversalOf, string? ipAddress, CancellationToken ct)
    {
        await _punishments.AppendAsync(PunishmentLog.Create(
            actorPlayerId: actorId == Guid.Empty ? null : actorId,
            actorRole: await ActorRoleSnapshotAsync(actorId, ct),
            targetPlayerId: target.Id,
            targetUsername: target.Username,
            type: type,
            reason: reason,
            expiresAt: expiresAt,
            reversalOfId: reversalOf,
            ipAddress: ipAddress), ct);
    }

    /// <summary>
    /// The actor's authority at the moment of the action, as a stored string rather than a join.
    /// Roles are grantable and revocable, so resolving the role at REVIEW time answers a different
    /// question from the one a dispute asks: a moderator later promoted to admin would appear to have
    /// acted with authority they did not have, and one later demoted would appear to have had none.
    ///
    /// Highest-first, because roles are additive flags and an admin who also holds Moderator acted as
    /// an admin.
    /// </summary>
    private async Task<string> ActorRoleSnapshotAsync(Guid actorId, CancellationToken ct)
    {
        if (actorId == Guid.Empty) return "System";

        var actor = await _players.FindByIdAsync(actorId, ct);
        if (actor is null) return "Unknown";
        if (actor.HasRole(PlayerRoles.Admin))     return "Admin";
        if (actor.HasRole(PlayerRoles.Moderator)) return "Moderator";
        return "Player";
    }

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
