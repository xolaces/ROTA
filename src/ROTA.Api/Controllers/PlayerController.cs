using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[ApiController]
[Route("api/players")]
[Authorize]
public sealed class PlayerController : ControllerBase
{
    private readonly IPlayerService _players;
    private readonly IValidator<UpdateUsernameRequest> _usernameValidator;
    private readonly IValidator<UpdateDisplayNameRequest> _displayNameValidator;

    public PlayerController(
        IPlayerService players,
        IValidator<UpdateUsernameRequest> usernameValidator,
        IValidator<UpdateDisplayNameRequest> displayNameValidator)
    {
        _players = players;
        _usernameValidator = usernameValidator;
        _displayNameValidator = displayNameValidator;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(PlayerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMe()
    {
        var result = await _players.GetProfileAsync(GetPlayerId());
        if (result is null) return NotFound();
        return Ok(result);
    }

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

    [HttpPut("me/display-name")]
    [ProducesResponseType(typeof(UpdateDisplayNameResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDisplayName(
        [FromBody] UpdateDisplayNameRequest request,
        CancellationToken ct)
    {
        var v = await _displayNameValidator.ValidateAsync(request, ct);
        if (!v.IsValid) return InvalidRequest(v);

        var result = await _players.UpdateDisplayNameAsync(GetPlayerId(), request, ct);

        return result.Status switch
        {
            DisplayNameUpdateStatus.Success  => Ok(new UpdateDisplayNameResponse(result.NewDisplayName!, result.UpdatedAt)),
            DisplayNameUpdateStatus.NotFound => NotFound(),
            _                               => StatusCode(500)
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
