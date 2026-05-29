using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

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

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ActiveRaidResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActive()
    {
        var result = await _raids.GetActiveRaidsAsync(GetPlayerId());
        return Ok(result);
    }

    // Direct summon is an admin/dev tool — players summon only via sigils (POST /api/items/{id}/use).
    // Config key: Admin:PlayerIds (see Program.cs and appsettings notes).
    [HttpPost("{raidDefinitionId}/summon")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(SummonRaidResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Summon(
        [FromRoute] string raidDefinitionId,
        [FromBody] SummonRaidRequest? request = null)
    {
        var diffStr = request?.Difficulty ?? "Normal";
        if (!Enum.TryParse<RaidDifficulty>(diffStr, ignoreCase: true, out var difficulty))
            return BadRequest(new { message = $"Invalid difficulty '{diffStr}'. Valid values: Normal, Hard, Legendary, Nightmare." });

        var result = await _raids.SummonRaidAsync(GetPlayerId(), raidDefinitionId, difficulty);

        if (result.Success)
            return StatusCode(StatusCodes.Status201Created, result.Response);

        return result.FailureCode switch
        {
            SummonRaidFailureCode.DefinitionNotFound => NotFound(new { message = result.FailureReason }),
            SummonRaidFailureCode.PlayerNotFound     => NotFound(new { message = result.FailureReason }),
            _                                        => UnprocessableEntity(new { message = result.FailureReason }),
        };
    }

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
