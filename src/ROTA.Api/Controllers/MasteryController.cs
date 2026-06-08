using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>System 22 Phase A — player-facing Masteries endpoints. Pledge (POST) lands in Slice 3.</summary>
[ApiController]
[Route("api/masteries")]
[Authorize]
public sealed class MasteryController : ControllerBase
{
    private readonly IMasteryService _masteries;

    public MasteryController(IMasteryService masteries) => _masteries = masteries;

    [HttpGet]
    [ProducesResponseType(typeof(MasteryOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => Ok(await _masteries.GetMasteriesAsync(GetPlayerId(), ct));

    // SECURITY: PlayerId always from the verified JWT sub claim.
    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
