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
    private readonly IValidator<UnmutePlayerRequest> _unmuteValidator;

    public ModerationController(
        IAdminService admin,
        IValidator<BanPlayerRequest> banValidator,
        IValidator<MutePlayerRequest> muteValidator,
        IValidator<UnbanPlayerRequest> unbanValidator,
        IValidator<UnmutePlayerRequest> unmuteValidator)
    {
        _admin = admin;
        _banValidator = banValidator;
        _muteValidator = muteValidator;
        _unbanValidator = unbanValidator;
        _unmuteValidator = unmuteValidator;
    }

    /// <summary>
    /// Bans a player. Reachable by Moderators AND Admins, because the authority split is a DURATION
    /// question the service settles: northstar §6 gives a Moderator up to three days and reserves
    /// permanent bans to Admins. The endpoint deliberately carries no AdminOnly policy — a blanket
    /// policy here could only re-impose the interim rule that made banning Admin-only (D-017).
    /// </summary>
    [HttpPost("players/{idOrUsername}/ban")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ban([FromRoute] string idOrUsername, [FromBody] BanPlayerRequest request)
    {
        var v = await _banValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);
        return Respond(
            await _admin.BanPlayerAsync(
                GetActorId(), idOrUsername, request.Reason, request.DurationDays, Ip()),
            "Player banned.");
    }

    /// <summary>
    /// Lifts a ban — the only in-product remedy for one. A Moderator may lift a TEMPORARY ban (the
    /// class they may issue); only an Admin may lift a permanent one. Enforced in the service, which
    /// is the layer that can see whether the ban is dated. Governance audit 2026-08-22.
    /// </summary>
    [HttpPost("players/{idOrUsername}/unban")]
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

    /// <summary>
    /// One player's moderation history, newest first — the read side of northstar §6. Logging every
    /// punishment achieves nothing for disputes if nobody can read the record back.
    ///
    /// Moderator-visible, not Admin-only: a moderator about to act on a player needs to see whether
    /// this is a first offence or a fifth, and that is the judgement §6 asks them to make.
    /// </summary>
    [HttpGet("players/{idOrUsername}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<PunishmentLogEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History([FromRoute] string idOrUsername, [FromQuery] int limit = 100)
    {
        var history = await _admin.GetPunishmentHistoryAsync(idOrUsername, limit);
        if (history is null) return NotFound(new { message = $"Player '{idOrUsername}' not found." });
        return Ok(history);
    }

    /// <summary>
    /// Lifts an active mute on a player. A reason is required, as it is for a ban lift. A Moderator may
    /// not lift a mute an Admin placed — enforced in the service, which is the layer that can read the
    /// mute's provenance out of punishment_log.
    /// </summary>
    [HttpPost("players/{idOrUsername}/unmute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unmute(
        [FromRoute] string idOrUsername, [FromBody] UnmutePlayerRequest request)
    {
        var v = await _unmuteValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);
        return Respond(
            await _admin.UnmutePlayerAsync(GetActorId(), idOrUsername, request.Reason, Ip()),
            "Player unmuted.");
    }

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
