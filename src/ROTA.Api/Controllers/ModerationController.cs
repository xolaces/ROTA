using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Moderator/admin punitive actions (T40): ban, mute, unmute. Every action is audited and raises a
/// ModerationAction operator email, creating a dispute-review trail visible in the ops dashboard.
/// </summary>
[ApiController]
[Route("api/moderation")]
[Authorize(Policy = "ModeratorOrAdmin")]
public sealed class ModerationController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly IValidator<BanPlayerRequest> _banValidator;
    private readonly IValidator<MutePlayerRequest> _muteValidator;
    private readonly IValidator<UnbanPlayerRequest> _unbanValidator;

    public ModerationController(
        IAdminService admin,
        IValidator<BanPlayerRequest> banValidator,
        IValidator<MutePlayerRequest> muteValidator,
        IValidator<UnbanPlayerRequest> unbanValidator)
    {
        _admin = admin;
        _banValidator = banValidator;
        _muteValidator = muteValidator;
        _unbanValidator = unbanValidator;
    }

    /// <summary>
    /// Bans a player. Admin-only: bans are permanent until temporary bans exist, and northstar §6
    /// reserves permanent bans to Admins. Moderators mute instead. Governance audit 2026-08-22.
    /// </summary>
    [HttpPost("players/{idOrUsername}/ban")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ban([FromRoute] string idOrUsername, [FromBody] BanPlayerRequest request)
    {
        var v = await _banValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);
        return Respond(await _admin.BanPlayerAsync(GetActorId(), idOrUsername, request.Reason, Ip()), "Player banned.");
    }

    /// <summary>
    /// Lifts a ban. Admin-only (enforced in the service) — this is the only in-product way to
    /// reverse a ban, which is otherwise permanent. Governance audit 2026-08-22.
    /// </summary>
    [HttpPost("players/{idOrUsername}/unban")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unban([FromRoute] string idOrUsername, [FromBody] UnbanPlayerRequest request)
    {
        var v = await _unbanValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);
        return Respond(await _admin.UnbanPlayerAsync(GetActorId(), idOrUsername, request.Reason, Ip()), "Ban lifted.");
    }

    /// <summary>Mutes a player's chat for a fixed duration.</summary>
    [HttpPost("players/{idOrUsername}/mute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Mute([FromRoute] string idOrUsername, [FromBody] MutePlayerRequest request)
    {
        var v = await _muteValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);
        return Respond(
            await _admin.MutePlayerAsync(GetActorId(), idOrUsername, request.DurationMinutes, request.Reason, Ip()),
            "Player muted.");
    }

    /// <summary>Lifts any active mute on a player.</summary>
    [HttpPost("players/{idOrUsername}/unmute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unmute([FromRoute] string idOrUsername)
        => Respond(await _admin.UnmutePlayerAsync(GetActorId(), idOrUsername, Ip()), "Player unmuted.");

    private IActionResult Respond(AdminActionResult result, string okMessage)
    {
        if (result.Success) return Ok(new { message = okMessage });
        if (result.FailureReason?.Contains("not found") == true)
            return NotFound(new { message = result.FailureReason });
        if (result.FailureReason?.Contains("not a moderator") == true)
            return Forbid();
        return BadRequest(new { message = result.FailureReason });
    }

    private IActionResult InvalidRequest(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private Guid GetActorId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
