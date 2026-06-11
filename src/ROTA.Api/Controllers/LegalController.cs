using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// T68 — terms-of-service / privacy-policy documents and acceptance. Documents are anonymous (the
/// client shows them on the registration screen, pre-auth); acceptance is authenticated.
/// </summary>
[ApiController]
[Route("api/legal")]
public sealed class LegalController : ControllerBase
{
    private readonly ILegalService _legal;

    public LegalController(ILegalService legal)
    {
        _legal = legal;
    }

    [HttpGet("terms")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LegalDocumentResponse), StatusCodes.Status200OK)]
    public IActionResult GetTerms() => Ok(_legal.GetTerms());

    [HttpGet("privacy")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LegalDocumentResponse), StatusCodes.Status200OK)]
    public IActionResult GetPrivacy() => Ok(_legal.GetPrivacy());

    /// <summary>Records acceptance of the CURRENT terms version. 409 = the submitted version is stale.</summary>
    [HttpPost("accept")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept([FromBody] AcceptTermsRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var status = await _legal.AcceptTermsAsync(GetPlayerId(), request.Version, ip);

        return status switch
        {
            AcceptTermsStatus.Success => NoContent(),
            AcceptTermsStatus.NotFound => NotFound(),
            _ => Conflict(new { message = "That terms version is not current — re-fetch /api/legal/terms." }),
        };
    }

    private Guid GetPlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
