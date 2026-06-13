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
    private readonly IGauntletScoringService _scoring;
    private readonly IStrikeRepository _strikes;
    private readonly IGauntletCurrencyRepository _currency;
    private readonly IValidator<BuyStrikesRequest> _buyValidator;
    private readonly IGauntletBattalionService _battalion;
    private readonly IValidator<SetGauntletBattalionRequest> _battalionValidator;

    public GauntletController(
        IGauntletService gauntlet,
        IGauntletScoringService scoring,
        IStrikeRepository strikes,
        IGauntletCurrencyRepository currency,
        IValidator<BuyStrikesRequest> buyValidator,
        IGauntletBattalionService battalion,
        IValidator<SetGauntletBattalionRequest> battalionValidator)
    {
        _gauntlet           = gauntlet;
        _scoring            = scoring;
        _strikes            = strikes;
        _currency           = currency;
        _buyValidator       = buyValidator;
        _battalion          = battalion;
        _battalionValidator = battalionValidator;
    }

    /// <summary>The caller's Gauntlet battalion (resolved units + computed power).</summary>
    [HttpGet("battalion")]
    [ProducesResponseType(typeof(GauntletBattalionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBattalion(CancellationToken ct)
        => Ok(await _battalion.GetBattalionAsync(PlayerId(), ct));

    /// <summary>
    /// Assign the caller's battalion loadout (≤6 generals + ≤20 troops from owned units).
    /// 200 + battalion · 400 malformed · 422 business rule (UnitNotOwned/WrongUnitType/DuplicateUnit).
    /// </summary>
    [HttpPut("battalion")]
    [ProducesResponseType(typeof(GauntletBattalionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetBattalion([FromBody] SetGauntletBattalionRequest request, CancellationToken ct)
    {
        var validation = await _battalionValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _battalion.SetBattalionAsync(PlayerId(), request.Generals, request.Troops, ct);
        return result.Status == BattalionUpdateStatus.Success
            ? Ok(result.Battalion)
            : UnprocessableEntity(new { failureCode = result.Status.ToString() });
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

        // T76 — the caller's "you placed #N" card from the most recently settled event (null when
        // none settled or the caller sat it out). Always populated so the client decides display.
        var lastSettlement = await _gauntlet.GetMyLastSettlementAsync(playerId, ct);

        return Ok(new GauntletOverviewResponse
        {
            CurrentEvent     = eventDto,
            MyEntry          = entryDto,
            StrikeBalance    = strikeBalance,
            TokenBalance     = tokenBalance,
            PitchforkBalance = pitchforkBalance,
            LastSettlement   = lastSettlement,
        });
    }

    /// <summary>
    /// T76 — the kind-aware prize preview table for the event page. <c>kind</c> optional: defaults
    /// to the active event's kind (Neck when no event is active). 400 on an unknown kind.
    /// </summary>
    [HttpGet("prizes")]
    [ProducesResponseType(typeof(GauntletPrizeTableResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPrizes([FromQuery] string? kind, CancellationToken ct)
    {
        GauntletEventKind? parsedKind = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<GauntletEventKind>(kind, ignoreCase: true, out var k)
                || !Enum.IsDefined(k))
            {
                return BadRequest(new
                {
                    message = $"Unknown kind '{kind}' ({string.Join(", ", Enum.GetNames<GauntletEventKind>())}).",
                });
            }
            parsedKind = k;
        }

        return Ok(await _gauntlet.GetPrizeTableAsync(parsedKind, ct));
    }

    /// <summary>
    /// The snapshot-ranked leaderboard for a single league plus the caller's own standing. Ranks are
    /// served from the ~60s Postgres snapshot (not recomputed on read). Returns an empty board for the
    /// league when no event is active. 400 on a missing/invalid <c>league</c>.
    /// </summary>
    [HttpGet("leaderboard")]
    [ProducesResponseType(typeof(GauntletLeaderboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLeaderboard([FromQuery] string? league, CancellationToken ct)
    {
        if (!Enum.TryParse<GauntletLeague>(league, ignoreCase: true, out var parsedLeague)
            || !Enum.IsDefined(parsedLeague))
        {
            return BadRequest(new
            {
                message = $"A valid 'league' query parameter is required ({string.Join(", ", Enum.GetNames<GauntletLeague>())}).",
            });
        }

        var activeEvent = await _gauntlet.GetCurrentEventAsync(ct);
        if (activeEvent is null)
        {
            // No event running → an empty board for the requested league (not an error).
            return Ok(new GauntletLeaderboardResponse { League = parsedLeague.ToString() });
        }

        var board = await _scoring.GetLeaderboardAsync(activeEvent.Id, parsedLeague, PlayerId(), ct);
        return Ok(board);
    }

    /// <summary>
    /// The caller's current Gauntlet ladder target (System 16 Slice 7). Auto-advancing: a defeated
    /// stage means the next is spawned lazily on this call. Always 200 — the response flags
    /// (NoActiveEvent / JoinedRequired / Complete / ActiveRaid) describe the state.
    /// </summary>
    [HttpGet("ladder")]
    [ProducesResponseType(typeof(GauntletLadderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLadder(CancellationToken ct)
        => Ok(await _gauntlet.GetLadderAsync(PlayerId(), ct));

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

    /// <summary>The token-shop catalogue + the caller's live Token and Pitchfork balances.</summary>
    [HttpGet("shop")]
    [ProducesResponseType(typeof(GauntletShopResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShop(CancellationToken ct)
        => Ok(await _gauntlet.GetShopAsync(PlayerId(), ct));

    /// <summary>
    /// Buys a token-shop entry (idempotent; Token vs Pitchfork isolated). Success/AlreadyCharged →
    /// 200; AlreadyOwned → 409; InsufficientTokens → 422; unknown entry → 404.
    /// </summary>
    [HttpPost("shop/{entryId}/buy")]
    [ProducesResponseType(typeof(BuyShopResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BuyFromShop(string entryId, CancellationToken ct)
    {
        var result = await _gauntlet.BuyFromShopAsync(PlayerId(), entryId, ct);

        // Success covers both a first-time purchase and an idempotent AlreadyCharged re-grant.
        if (result.Success)
            return Ok(result);
        if (result.AlreadyOwned)
            return Conflict(result);
        if (result.InsufficientTokens)
            return UnprocessableEntity(result);

        // Remaining failure = unknown entry id (NotFound-style).
        return NotFound(new { message = result.FailureReason });
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
