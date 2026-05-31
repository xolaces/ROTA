using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[ApiController]
[Route("api/magics")]
[Authorize]
public sealed class MagicController : ControllerBase
{
    private readonly IMagicService _magics;

    public MagicController(IMagicService magics) => _magics = magics;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OwnedMagicResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOwned()
    {
        var result = await _magics.GetOwnedMagicsAsync(GetPlayerId());
        return Ok(result);
    }

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
