using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[ApiController]
[Authorize]
public sealed class MagicController : ControllerBase
{
    private readonly IMagicService _magics;

    public MagicController(IMagicService magics) => _magics = magics;

    [HttpGet("api/magics")]
    [ProducesResponseType(typeof(IReadOnlyList<OwnedMagicResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOwned()
    {
        var result = await _magics.GetOwnedMagicsAsync(GetPlayerId());
        return Ok(result);
    }

    [HttpPost("api/raids/{raidId:guid}/magics")]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyMagic(
        [FromRoute] Guid raidId,
        [FromBody] ApplyMagicRequest request)
    {
        bool isAdmin = User.IsInRole("Admin");
        var result = await _magics.ApplyMagicAsync(GetPlayerId(), raidId, request.MagicDefinitionId, isAdmin);
        if (result.Success) return Ok(result);

        return result.FailureCode switch
        {
            MagicApplyFailureCode.RaidNotFound      => NotFound(result),
            MagicApplyFailureCode.RaidNotActive     => UnprocessableEntity(result),
            MagicApplyFailureCode.WorldGateBlocked  => Forbid(),
            MagicApplyFailureCode.MagicNotOwned     => UnprocessableEntity(result),
            MagicApplyFailureCode.NotAParticipant   => Forbid(),
            MagicApplyFailureCode.AlreadyAppliedByPlayer => Conflict(result),
            MagicApplyFailureCode.DuplicateMagic    => Conflict(result),
            MagicApplyFailureCode.SlotsFull         => UnprocessableEntity(result),
            _                                       => BadRequest(result),
        };
    }

    [HttpDelete("api/raids/{raidId:guid}/magics/{magicDefinitionId}")]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MagicApplyResult), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMagic(
        [FromRoute] Guid raidId,
        [FromRoute] string magicDefinitionId)
    {
        bool isAdmin = User.IsInRole("Admin");
        var result = await _magics.RemoveMagicAsync(GetPlayerId(), raidId, magicDefinitionId, isAdmin);
        if (result.Success) return Ok(result);

        return result.FailureCode switch
        {
            MagicApplyFailureCode.RaidNotFound    => NotFound(result),
            MagicApplyFailureCode.MagicNotFound   => NotFound(result),
            MagicApplyFailureCode.WorldGateBlocked => Forbid(),
            MagicApplyFailureCode.RemoveNotAllowed => Forbid(),
            _                                      => BadRequest(result),
        };
    }

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
