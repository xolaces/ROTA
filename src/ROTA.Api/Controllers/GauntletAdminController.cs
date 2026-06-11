using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Admin-only Gauntlet event lifecycle (System 16 Slice 2): open / close / settle. Re-verifies the
/// actor from the DB on every mutation (same pattern as AdminController). Settlement payout is
/// Slice 5; settle here transitions state only.
/// </summary>
[ApiController]
[Route("api/admin/gauntlet")]
[Authorize(Policy = "AdminOnly")]
public sealed class GauntletAdminController : ControllerBase
{
    private readonly IGauntletAdminService _admin;
    private readonly IPlayerRepository _players;
    private readonly IValidator<OpenGauntletEventRequest> _openValidator;

    public GauntletAdminController(
        IGauntletAdminService admin,
        IPlayerRepository players,
        IValidator<OpenGauntletEventRequest> openValidator)
    {
        _admin         = admin;
        _players       = players;
        _openValidator = openValidator;
    }

    /// <summary>Opens (creates + activates) a new event. Enforces ≤1 active.</summary>
    [HttpPost("events")]
    [ProducesResponseType(typeof(GauntletEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenEvent([FromBody] OpenGauntletEventRequest request, CancellationToken ct)
    {
        var v = await _openValidator.ValidateAsync(request, ct);
        if (!v.IsValid) return Invalid(v);

        if (!await ActorIsAdminAsync(ct)) return Forbid();

        // T76 — kind is validated by the request validator (must parse to GauntletEventKind).
        var kind = Enum.Parse<GauntletEventKind>(request.Kind, ignoreCase: true);
        var result = await _admin.OpenEventAsync(
            request.Name, request.StartsAt, request.EndsAt, kind, request.LoreBlurb, request.BannerKey, ct);
        if (result.Success) return Ok(result.Event);

        // ≤1-active rejection is a conflict; everything else is a bad request.
        if (result.FailureReason?.Contains("already exists") == true)
            return Conflict(new { message = result.FailureReason });
        return BadRequest(new { message = result.FailureReason });
    }

    /// <summary>Closes an active event (guard: must be Active).</summary>
    [HttpPost("events/{id:guid}/close")]
    [ProducesResponseType(typeof(GauntletEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseEvent(Guid id, CancellationToken ct)
    {
        if (!await ActorIsAdminAsync(ct)) return Forbid();

        var result = await _admin.CloseEventAsync(id, ct);
        return RespondLifecycle(result);
    }

    /// <summary>Settles a closed event (state-only here; idempotent — no-op if already settled).</summary>
    [HttpPost("events/{id:guid}/settle")]
    [ProducesResponseType(typeof(GauntletEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SettleEvent(Guid id, CancellationToken ct)
    {
        if (!await ActorIsAdminAsync(ct)) return Forbid();

        var result = await _admin.SettleEventAsync(id, ct);
        return RespondLifecycle(result);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private IActionResult RespondLifecycle(GauntletEventActionResult result)
    {
        if (result.Success) return Ok(result.Event);
        if (result.FailureReason?.Contains("not found") == true)
            return NotFound(new { message = result.FailureReason });
        return BadRequest(new { message = result.FailureReason });
    }

    private async Task<bool> ActorIsAdminAsync(CancellationToken ct)
    {
        var actor = await _players.FindByIdAsync(GetActorId(), ct);
        return actor is not null && actor.HasRole(PlayerRoles.Admin);
    }

    private Guid GetActorId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private IActionResult Invalid(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }
}
