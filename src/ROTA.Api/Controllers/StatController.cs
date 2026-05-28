using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[ApiController]
[Route("api/stats")]
[Authorize]
public sealed class StatController : ControllerBase
{
    private readonly IStatService _stats;
    private readonly IValidator<AllocateStatRequest> _allocateValidator;

    public StatController(IStatService stats, IValidator<AllocateStatRequest> allocateValidator)
    {
        _stats             = stats;
        _allocateValidator = allocateValidator;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(PlayerStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStats()
    {
        var result = await _stats.GetStatsAsync(GetPlayerId());
        if (result is null)
            return NotFound(new { message = "Player stats not found." });

        return Ok(result);
    }

    [HttpPost("allocate")]
    [ProducesResponseType(typeof(AllocateStatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AllocateStatResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AllocateStat([FromBody] AllocateStatRequest request)
    {
        var v = await _allocateValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        // Validator guarantees StatType is a valid enum name — parse is safe.
        var statType = Enum.Parse<StatType>(request.StatType, ignoreCase: true);

        var result = await _stats.AllocateStatPointAsync(GetPlayerId(), statType, request.Amount);

        if (!result.Success)
            return UnprocessableEntity(result);

        return Ok(result);
    }

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private IActionResult InvalidRequest(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }
}
