using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>System 22 Phase A — player-facing Masteries endpoints.</summary>
[ApiController]
[Route("api/masteries")]
[Authorize]
public sealed class MasteryController : ControllerBase
{
    private readonly IMasteryService _masteries;
    private readonly IValidator<PledgeRequest> _pledgeValidator;

    public MasteryController(IMasteryService masteries, IValidator<PledgeRequest> pledgeValidator)
    {
        _masteries = masteries;
        _pledgeValidator = pledgeValidator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(MasteryOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => Ok(await _masteries.GetMasteriesAsync(GetPlayerId(), ct));

    /// <summary>Pledge an Ancient (LOSSLESS re-spec). Free first-pledge / monthly / paid-weekly resolved server-side.</summary>
    [HttpPost("pledge")]
    [ProducesResponseType(typeof(MasteryRespecResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Pledge([FromBody] PledgeRequest request, CancellationToken ct)
    {
        var v = await _pledgeValidator.ValidateAsync(request, ct);
        if (!v.IsValid) return InvalidRequest(v);

        var ancient = Enum.Parse<MasteryAncient>(request.Ancient, ignoreCase: true);
        var result = await _masteries.RespecAsync(GetPlayerId(), ancient, ct);

        if (result.Success) return Ok(result);

        return result.FailureCode switch
        {
            MasteryRespecFailureCode.AncientNotFound  => NotFound(new { message = result.FailureReason }),
            MasteryRespecFailureCode.PlayerNotFound   => NotFound(new { message = result.FailureReason }),
            MasteryRespecFailureCode.AlreadyPledged   => Conflict(new { message = result.FailureReason }),
            MasteryRespecFailureCode.WeeklyCapReached => Conflict(new { message = result.FailureReason }),
            MasteryRespecFailureCode.InsufficientGems => UnprocessableEntity(new { message = result.FailureReason }),
            _                                         => BadRequest(new { message = result.FailureReason }),
        };
    }

    // SECURITY: PlayerId always from the verified JWT sub claim.
    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private IActionResult InvalidRequest(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }
}
