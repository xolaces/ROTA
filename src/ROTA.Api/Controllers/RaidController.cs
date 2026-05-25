using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

// BETA — thin controller, zero business logic. PlayerId always from verified JWT.
[ApiController]
[Route("api/raids")]
[Authorize]
public sealed class RaidController : ControllerBase
{
    private readonly IRaidService _raids;

    public RaidController(IRaidService raids)
    {
        _raids = raids;
    }

    /// <summary>Returns all active (non-defeated, non-expired) raids with the caller's damage fields.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ActiveRaidResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActive()
    {
        var result = await _raids.GetActiveRaidsAsync(GetPlayerId());
        return Ok(result);
    }

    /// <summary>Summons a new raid from the specified static definition.</summary>
    [HttpPost("{raidDefinitionId}/summon")]
    [ProducesResponseType(typeof(SummonRaidResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Summon([FromRoute] string raidDefinitionId)
    {
        var result = await _raids.SummonRaidAsync(GetPlayerId(), raidDefinitionId);

        if (result.Success)
            return StatusCode(StatusCodes.Status201Created, result.Response);

        return result.FailureCode switch
        {
            SummonRaidFailureCode.DefinitionNotFound => NotFound(new { message = result.FailureReason }),
            SummonRaidFailureCode.PlayerNotFound     => NotFound(new { message = result.FailureReason }),
            _                                        => UnprocessableEntity(new { message = result.FailureReason }),
        };
    }

    /// <summary>
    /// Hits an active raid with the specified stamina multiplier.
    /// Body: { "hitSize": 1|5|20, "idempotencyKey": "client-generated-uuid" }
    /// Duplicate idempotencyKey returns the cached response with zero reprocessing.
    /// </summary>
    [HttpPost("{activeRaidId}/hit")]
    [ProducesResponseType(typeof(RaidHitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Hit([FromRoute] Guid activeRaidId, [FromBody] RaidHitRequest request)
    {
        var result = await _raids.HitRaidAsync(GetPlayerId(), activeRaidId, request.HitSize, request.IdempotencyKey);

        if (result.Success)
            return Ok(result.Response);

        return result.FailureCode switch
        {
            RaidHitFailureCode.RaidNotFound        => NotFound(new { message = result.FailureReason }),
            RaidHitFailureCode.RaidExpired         => StatusCode(StatusCodes.Status410Gone, new { message = result.FailureReason }),
            RaidHitFailureCode.RaidAlreadyDefeated => Conflict(new { message = result.FailureReason }),
            RaidHitFailureCode.InvalidHitSize      => BadRequest(new { message = result.FailureReason }),
            _                                      => UnprocessableEntity(new { message = result.FailureReason }),
        };
    }

    // SECURITY: PlayerId always from verified JWT sub claim — never from route or body.
    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
