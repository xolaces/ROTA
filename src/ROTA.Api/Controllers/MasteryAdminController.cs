using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Admin-only Masteries operations (System 22 Phase A). Mirrors the Stat-board refresh: DB actor
/// re-verify, run the snapshot, write an audit row.
/// </summary>
[ApiController]
[Route("api/admin/masteries")]
[Authorize(Policy = "AdminOnly")]
public sealed class MasteryAdminController : ControllerBase
{
    private readonly IMasteryService _masteries;
    private readonly IPlayerRepository _players;
    private readonly IAuditLogRepository _auditLog;

    public MasteryAdminController(
        IMasteryService masteries,
        IPlayerRepository players,
        IAuditLogRepository auditLog)
    {
        _masteries = masteries;
        _players   = players;
        _auditLog  = auditLog;
    }

    /// <summary>Refreshes the MasteryRating leaderboard boards (Active + Lifetime, Live snapshot).</summary>
    [HttpPost("rating/refresh")]
    [ProducesResponseType(typeof(MasteryRatingRefreshResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RefreshRatingBoard()
    {
        var actorId = GetActorId();

        // Re-verify actor from DB (same pattern as AdminController.RefreshStatBoards).
        var actor = await _players.FindByIdAsync(actorId);
        if (actor is null || !actor.HasRole(PlayerRoles.Admin))
            return Forbid();

        var snapshotAt = DateTimeOffset.UtcNow;
        var count      = await _masteries.SnapshotRatingBoardAsync();

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId,
            "MasteryRatingRefreshed",
            inputHash: null,
            resultSummary: $"actor={actorId} players_snapshotted={count} snapshot_at={snapshotAt:O}",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()));

        return Ok(new MasteryRatingRefreshResponse
        {
            PlayersSnapshotted = count,
            SnapshotAt         = snapshotAt,
        });
    }

    /// <summary>
    /// DEV/ADMIN — force one or more Ancients' levels (1..5, up or down) for testing. Target defaults
    /// to the calling admin's own account. The client's dev-force tool gates this behind a confirm dialog
    /// when running live; mock fakes it locally.
    /// </summary>
    [HttpPost("force")]
    [ProducesResponseType(typeof(MasteryOverviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForceLevels([FromBody] MasteryForceRequest request, CancellationToken ct)
    {
        var actorId = GetActorId();
        var actor = await _players.FindByIdAsync(actorId, ct);
        if (actor is null || !actor.HasRole(PlayerRoles.Admin))
            return Forbid();

        var levels = new Dictionary<MasteryAncient, int>();
        foreach (var (key, value) in request.Levels)
        {
            if (!Enum.TryParse<MasteryAncient>(key, ignoreCase: true, out var ancient))
                return BadRequest(new { message = $"Unknown Ancient '{key}'." });
            if (value < 1 || value > 5)
                return BadRequest(new { message = $"Level for '{key}' must be 1..5 (was {value})." });
            levels[ancient] = value;
        }
        if (levels.Count == 0)
            return BadRequest(new { message = "No levels supplied." });

        // Resolve the target (default: the calling admin).
        Guid targetId = actorId;
        if (!string.IsNullOrWhiteSpace(request.TargetPlayerIdOrUsername))
        {
            var target = Guid.TryParse(request.TargetPlayerIdOrUsername, out var g)
                ? await _players.FindByIdAsync(g, ct)
                : await _players.FindByUsernameAsync(request.TargetPlayerIdOrUsername, ct);
            if (target is null)
                return NotFound(new { message = $"Player '{request.TargetPlayerIdOrUsername}' not found." });
            targetId = target.Id;
        }

        var overview = await _masteries.ForceLevelsAsync(targetId, levels, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId, "MasteryForceLevels", inputHash: null,
            resultSummary: $"actor={actorId} target={targetId} levels={string.Join(",", levels.Select(kv => $"{kv.Key}:{kv.Value}"))}",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

        return Ok(overview);
    }

    private Guid GetActorId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
