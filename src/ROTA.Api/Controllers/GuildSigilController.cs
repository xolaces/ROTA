using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Guild sigil economy (System 21 Slice 3a): once-daily claim, buy sigils with shop tickets, donate
/// sigils to the guild pool, and read balances. Thin: PlayerId comes from the JWT sub claim; the caller's
/// guild is resolved server-side in <see cref="IGuildEconomyService"/>. All failure codes are conflicts
/// (not-in-guild / daily cap / insufficient balance / race) → 409.
/// </summary>
[ApiController]
[Route("api/guilds/sigils")]
[Authorize]
public sealed class GuildSigilController : ControllerBase
{
    private readonly IGuildEconomyService _economy;

    public GuildSigilController(IGuildEconomyService economy) => _economy = economy;

    [HttpGet("balances")]
    [ProducesResponseType(typeof(GuildSigilBalances), StatusCodes.Status200OK)]
    public async Task<ActionResult<GuildSigilBalances>> Balances()
        => Ok(await _economy.GetBalancesAsync(PlayerId()));

    [HttpPost("claim")]
    [ProducesResponseType(typeof(GuildClaimResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Claim()
    {
        var r = await _economy.ClaimDailyAsync(PlayerId());
        return r.Success ? Ok(r) : MapFailure(r.FailureCode, r.FailureReason);
    }

    [HttpPost("buy")]
    [ProducesResponseType(typeof(GuildBuyResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Buy()
    {
        var r = await _economy.BuySigilAsync(PlayerId());
        return r.Success ? Ok(r) : MapFailure(r.FailureCode, r.FailureReason);
    }

    [HttpPost("donate")]
    [ProducesResponseType(typeof(GuildDonateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Donate()
    {
        var r = await _economy.DonateSigilAsync(PlayerId());
        return r.Success ? Ok(r) : MapFailure(r.FailureCode, r.FailureReason);
    }

    private IActionResult MapFailure(GuildEconomyFailureCode code, string? reason)
        => StatusCode(StatusCodes.Status409Conflict, new { code = code.ToString(), message = reason ?? "Conflict" });

    private Guid PlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
