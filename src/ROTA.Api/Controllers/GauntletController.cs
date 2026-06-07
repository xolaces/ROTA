using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Player-facing Gauntlet endpoints (System 16 Slice 2): read the current event + the caller's
/// standing and balances, join (idempotent), and buy Strikes with gems (idempotent). PlayerId is
/// always the verified JWT <c>sub</c>.
/// </summary>
[ApiController]
[Route("api/gauntlet")]
[Authorize]
public sealed class GauntletController : ControllerBase
{
    private readonly IGauntletService _gauntlet;
    private readonly IStrikeRepository _strikes;
    private readonly IGauntletCurrencyRepository _currency;
    private readonly IValidator<BuyStrikesRequest> _buyValidator;

    public GauntletController(
        IGauntletService gauntlet,
        IStrikeRepository strikes,
        IGauntletCurrencyRepository currency,
        IValidator<BuyStrikesRequest> buyValidator)
    {
        _gauntlet     = gauntlet;
        _strikes      = strikes;
        _currency     = currency;
        _buyValidator = buyValidator;
    }

    /// <summary>Current event (null if none active) + the caller's entry/league + balances.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GauntletOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        var playerId = PlayerId();

        // Service returns DTOs (the entity→DTO mapping lives in the Application layer, not here).
        var eventDto = await _gauntlet.GetCurrentEventAsync(ct);
        GauntletEntryResponse? entryDto = eventDto is null
            ? null
            : await _gauntlet.GetMyEntryAsync(playerId, eventDto.Id, ct);

        var strikeBalance    = await _strikes.GetBalanceAsync(playerId, ct);
        var tokenBalance     = await _currency.GetBalanceAsync(playerId, GauntletCurrency.Token, ct);
        var pitchforkBalance = await _currency.GetBalanceAsync(playerId, GauntletCurrency.Pitchfork, ct);

        return Ok(new GauntletOverviewResponse
        {
            CurrentEvent     = eventDto,
            MyEntry          = entryDto,
            StrikeBalance    = strikeBalance,
            TokenBalance     = tokenBalance,
            PitchforkBalance = pitchforkBalance,
        });
    }

    /// <summary>Joins the active event (idempotent). League is locked at first join.</summary>
    [HttpPost("join")]
    [ProducesResponseType(typeof(GauntletEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Join(CancellationToken ct)
    {
        var result = await _gauntlet.JoinEventAsync(PlayerId(), ct);
        if (result.Success)
            return Ok(result.Entry);

        if (result.FailureReason?.Contains("no active") == true)
            return NotFound(new { message = result.FailureReason });
        return BadRequest(new { message = result.FailureReason });
    }

    /// <summary>Buys Strikes with gems (uncapped, idempotent on the client idempotency key).</summary>
    [HttpPost("strikes/buy")]
    [ProducesResponseType(typeof(BuyStrikesResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BuyStrikes([FromBody] BuyStrikesRequest request, CancellationToken ct)
    {
        var v = await _buyValidator.ValidateAsync(request, ct);
        if (!v.IsValid) return Invalid(v);

        var result = await _gauntlet.BuyStrikesAsync(PlayerId(), request.Strikes, request.IdempotencyKey, ct);
        if (!result.Success)
            return BadRequest(new { message = result.FailureReason, gemCost = result.GemCost });

        return Ok(result);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private IActionResult Invalid(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }

    private Guid PlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
