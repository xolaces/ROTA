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
    private readonly IClassService _classes;
    private readonly IValidator<AllocateStatRequest> _allocateValidator;
    private readonly IValidator<AllocateStatsRequest> _allocateManyValidator;

    public StatController(
        IStatService stats,
        IClassService classes,
        IValidator<AllocateStatRequest> allocateValidator,
        IValidator<AllocateStatsRequest> allocateManyValidator)
    {
        _stats             = stats;
        _classes           = classes;
        _allocateValidator = allocateValidator;
        _allocateManyValidator = allocateManyValidator;
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

    /// <summary>Several stats at once, all or nothing. 422 means nothing was spent.</summary>
    [HttpPost("allocate-many")]
    [ProducesResponseType(typeof(AllocateStatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AllocateStatResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AllocateStats([FromBody] AllocateStatsRequest request)
    {
        var v = await _allocateManyValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var set = request.Allocations
            .Select(a => (Enum.Parse<StatType>(a.StatType, ignoreCase: true), a.Amount))
            .ToList();
        var result = await _stats.AllocateStatPointsAsync(GetPlayerId(), set);
        return result.Success ? Ok(result) : UnprocessableEntity(result);
    }

    [HttpGet("class")]
    [ProducesResponseType(typeof(ClassRegenRates), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClass()
    {
        var result = await _classes.GetClassInfoAsync(GetPlayerId());
        if (result is null)
            return NotFound(new { message = "Player not found." });

        return Ok(result);
    }

    [HttpPost("class/choose")]
    [ProducesResponseType(typeof(ClassRegenRates), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChooseClass([FromBody] ChooseClassRequest request)
    {
        if (!Enum.TryParse<PlayerClass>(request.PlayerClass, ignoreCase: true, out var playerClass))
            return BadRequest(new { message = $"Invalid class '{request.PlayerClass}'." });

        var result = await _classes.AssignClassAsync(GetPlayerId(), playerClass);
        if (result is null)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Class is not available at your current level or progression." });

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
