using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// the player market (prototype).
///
/// Every route derives the acting player from the VERIFIED JWT subject and nothing else. There is no
/// endpoint here that names a counterparty, because there is no direct transfer to name one for: a
/// seller posts to a public board and a buyer takes from it. The two sides never address each other.
/// </summary>
[ApiController]
[Route("api/market")]
[Authorize]
public sealed class MarketController : ControllerBase
{
    private readonly IMarketService _market;

    public MarketController(IMarketService market)
    {
        _market = market;
    }

    /// <summary>The public board: active, unexpired listings, cheapest first.</summary>
    [HttpGet("listings")]
    [ProducesResponseType(typeof(MarketBrowseResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Browse(
        [FromQuery] string? kind,
        [FromQuery] string? definitionId,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
        => Ok(await _market.BrowseAsync(GetPlayerId(), kind, definitionId, page, ct));

    /// <summary>The caller's own listings, any status.</summary>
    [HttpGet("listings/mine")]
    [ProducesResponseType(typeof(MarketBrowseResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken ct = default)
        => Ok(await _market.GetMyListingsAsync(GetPlayerId(), ct));

    /// <summary>Posts a stack to the board. The goods leave the caller's inventory immediately.</summary>
    [HttpPost("listings")]
    [ProducesResponseType(typeof(CreateListingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CreateListingResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CreateListingResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CreateListingResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(CreateListingResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateListingRequest request, CancellationToken ct = default)
    {
        var result = await _market.CreateListingAsync(GetPlayerId(), request, ct);
        return result.Success ? Ok(result) : StatusFor(result.FailureCode, result);
    }

    /// <summary>Takes a listing off the board and pays for it.</summary>
    [HttpPost("listings/{listingId:guid}/buy")]
    [ProducesResponseType(typeof(BuyListingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BuyListingResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(BuyListingResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(BuyListingResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(BuyListingResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(BuyListingResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Buy([FromRoute] Guid listingId, CancellationToken ct = default)
    {
        var result = await _market.BuyAsync(GetPlayerId(), listingId, ct);
        return result.Success ? Ok(result) : StatusFor(result.FailureCode, result);
    }

    /// <summary>Withdraws the caller's own unsold listing. The listing fee is not refunded.</summary>
    [HttpPost("listings/{listingId:guid}/cancel")]
    [ProducesResponseType(typeof(CancelListingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CancelListingResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CancelListingResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CancelListingResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromRoute] Guid listingId, CancellationToken ct = default)
    {
        var result = await _market.CancelAsync(GetPlayerId(), listingId, ct);
        return result.Success ? Ok(result) : StatusFor(result.FailureCode, result);
    }

    // One mapping for every route, so a refusal means the same thing whichever call produced it.
    // NotYours is 404, not 403: telling a stranger "that listing exists but is not yours" is a probe
    // oracle, and there is nothing they can do with either answer.
    private IActionResult StatusFor(MarketFailureCode code, object body) => code switch
    {
        MarketFailureCode.NotFound                    => NotFound(body),
        MarketFailureCode.NotYours                    => NotFound(body),
        MarketFailureCode.NoLongerActive              => Conflict(body),
        MarketFailureCode.MarketDisabled              => StatusCode(StatusCodes.Status503ServiceUnavailable, body),
        MarketFailureCode.LevelTooLow                 => StatusCode(StatusCodes.Status403Forbidden, body),
        MarketFailureCode.AccountTooNew               => StatusCode(StatusCodes.Status403Forbidden, body),
        MarketFailureCode.TradingRestricted           => StatusCode(StatusCodes.Status403Forbidden, body),
        MarketFailureCode.InsufficientGold            => UnprocessableEntity(body),
        MarketFailureCode.InsufficientQuantity        => UnprocessableEntity(body),
        MarketFailureCode.DailyGoldReceivedCapReached => Conflict(body),
        MarketFailureCode.DailySpendCapReached        => Conflict(body),
        MarketFailureCode.TooManyActiveListings       => Conflict(body),
        _                                             => BadRequest(body),
    };

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
