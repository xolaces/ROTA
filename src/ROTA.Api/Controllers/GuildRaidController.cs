using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Guild raids (System 21 Slice 3b): list the caller's guild's active raids and summon a new one
/// (officer-gated, consumes 1 pooled sigil). Members HIT guild raids through the existing
/// <c>POST /api/raids/{id}/hit</c> endpoint — HitRaidAsync forks on guild_id to spend GuildStamina, so
/// there is no parallel combat path here. Thin: PlayerId comes from the JWT sub; the caller's guild and
/// officer permission are resolved server-side in <see cref="IRaidService"/>.
/// </summary>
[ApiController]
[Route("api/guilds/raids")]
[Authorize]
public sealed class GuildRaidController : ControllerBase
{
    private readonly IRaidService _raids;

    public GuildRaidController(IRaidService raids) => _raids = raids;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ActiveRaidResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ActiveRaidResponse>>> List()
        => Ok(await _raids.GetGuildRaidsAsync(PlayerId()));

    [HttpPost("summon")]
    [ProducesResponseType(typeof(SummonGuildRaidResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Summon([FromBody] SummonGuildRaidRequest req)
    {
        if (!Enum.TryParse<RaidDifficulty>(req.Difficulty, ignoreCase: true, out var difficulty))
            return BadRequest(new { message = $"Invalid difficulty '{req.Difficulty}'. Valid: Normal, Hard, Legendary, Nightmare." });

        var result = await _raids.SummonGuildRaidAsync(PlayerId(), req.RaidDefinitionId, difficulty);
        if (result.Success)
            return StatusCode(StatusCodes.Status201Created, result);

        return result.FailureCode switch
        {
            GuildRaidFailureCode.PermissionDenied   => StatusCode(StatusCodes.Status403Forbidden, new { message = result.FailureReason }),
            GuildRaidFailureCode.DefinitionNotFound => NotFound(new { message = result.FailureReason }),
            // NotInGuild / InsufficientPool / Conflict
            _                                       => Conflict(new { message = result.FailureReason }),
        };
    }

    private Guid PlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
