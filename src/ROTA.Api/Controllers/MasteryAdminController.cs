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

    private Guid GetActorId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
