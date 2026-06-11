using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// T75 — dev/admin grant actions for the client Dev Tools screen. AdminOnly (role claim or
/// break-glass allowlist); every action is audited with the acting admin's id. These bypass the
/// normal earn loops by design — beta tooling, not gameplay surface.
/// </summary>
[ApiController]
[Route("api/dev")]
[Authorize(Policy = "AdminOnly")]
public sealed class DevController : ControllerBase
{
    private readonly IDevService _dev;

    public DevController(IDevService dev)
    {
        _dev = dev;
    }

    [HttpPost("grant")]
    [ProducesResponseType(typeof(DevActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Grant([FromBody] DevGrantRequest request)
    {
        var result = await _dev.GrantAsync(GetPlayerId(), request, GetIp());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("grant-item")]
    [ProducesResponseType(typeof(DevActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GrantItem([FromBody] DevGrantItemRequest request)
    {
        var result = await _dev.GrantItemAsync(GetPlayerId(), request, GetIp());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("refill")]
    [ProducesResponseType(typeof(DevActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Refill([FromBody] DevRefillRequest request)
    {
        var result = await _dev.RefillAsync(GetPlayerId(), request, GetIp());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    private Guid GetPlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    private string GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
