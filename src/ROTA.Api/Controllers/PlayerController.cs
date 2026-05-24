using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

// BETA — thin controller, zero business logic. PlayerId always sourced from verified JWT.
[ApiController]
[Route("api/players")]
[Authorize]
public sealed class PlayerController : ControllerBase
{
    private readonly IPlayerService _players;
    private readonly IValidator<UpdateUsernameRequest> _usernameValidator;

    public PlayerController(IPlayerService players, IValidator<UpdateUsernameRequest> usernameValidator)
    {
        _players = players;
        _usernameValidator = usernameValidator;
    }

    /// <summary>Returns the authenticated player's full profile with live resource values.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(PlayerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMe()
    {
        var result = await _players.GetProfileAsync(GetPlayerId());
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>Updates the authenticated player's username. Cannot change email or password.</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UpdateUsernameResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateUsernameRequest request)
    {
        var v = await _usernameValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var result = await _players.UpdateUsernameAsync(GetPlayerId(), request);

        return result.Status switch
        {
            PlayerUpdateStatus.Success => Ok(new UpdateUsernameResponse
            {
                Username = result.NewUsername!,
                UpdatedAt = result.UpdatedAt
            }),
            PlayerUpdateStatus.NotFound => NotFound(),
            PlayerUpdateStatus.UsernameTaken => Conflict(new { message = "Username is already taken." }),
            _ => StatusCode(500)
        };
    }

    // SECURITY: PlayerId always from verified JWT sub claim — never from request body or route.
    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private IActionResult InvalidRequest(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }
}
