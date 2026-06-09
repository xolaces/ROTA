using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>TICKET 46 — player-facing Achievement endpoints.</summary>
[ApiController]
[Route("api/achievements")]
[Authorize]
public sealed class AchievementController : ControllerBase
{
    private readonly IAchievementService _achievements;

    public AchievementController(IAchievementService achievements)
        => _achievements = achievements;

    [HttpGet]
    [ProducesResponseType(typeof(AchievementOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => Ok(await _achievements.GetForPlayerAsync(GetPlayerId(), ct));

    // SECURITY: PlayerId always from the verified JWT sub claim.
    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
