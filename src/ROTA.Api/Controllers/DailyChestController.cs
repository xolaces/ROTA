using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>System 29 — the free daily chest: six closed chests, pick one, all six revealed.</summary>
[ApiController]
[Route("api/daily-chest")]
[Authorize]
public sealed class DailyChestController : ControllerBase
{
    private readonly IDailyChestService _chest;
    private readonly IValidator<PickDailyChestRequest> _pickValidator;

    public DailyChestController(IDailyChestService chest, IValidator<PickDailyChestRequest> pickValidator)
    {
        _chest = chest;
        _pickValidator = pickValidator;
    }

    /// <summary>Today's chest. Contents stay hidden until the pick.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DailyChestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetToday()
        => Ok(await _chest.GetTodayAsync(GetPlayerId()));

    /// <summary>Opens chest <paramref name="index"/> (0–5) and reveals all six.</summary>
    [HttpPost("{index:int}/pick")]
    [ProducesResponseType(typeof(PickDailyChestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PickDailyChestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(PickDailyChestResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pick([FromRoute] int index)
    {
        var request = new PickDailyChestRequest { Index = index };
        var v = await _pickValidator.ValidateAsync(request);
        if (!v.IsValid)
            return BadRequest(new PickDailyChestResponse
            {
                FailureCode   = DailyChestFailureCode.InvalidIndex,
                FailureReason = v.Errors[0].ErrorMessage,
            });

        var result = await _chest.PickAsync(GetPlayerId(), request.Index);
        if (result.Success) return Ok(result);

        return result.FailureCode switch
        {
            DailyChestFailureCode.AlreadyPicked => Conflict(result),
            _                                   => BadRequest(result),
        };
    }

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
