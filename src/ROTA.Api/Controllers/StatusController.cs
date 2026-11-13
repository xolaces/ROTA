using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;

namespace ROTA.Api.Controllers;

/// <summary>
/// What the owner is telling players right now. Anonymous and open during maintenance: the client
/// reads it for the message of the day, and polls it while closed to know when to come back.
/// </summary>
[ApiController]
[Route("api/status")]
[AllowAnonymous]
public sealed class StatusController : ControllerBase
{
    private readonly IServiceNoticeStore _notices;

    public StatusController(IServiceNoticeStore notices) => _notices = notices;

    [HttpGet]
    [ProducesResponseType(typeof(ServiceStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var n = await _notices.GetAsync(ct);
        return Ok(new ServiceStatusResponse(n.Maintenance, n.MaintenanceMessage, n.Motd));
    }
}

public sealed record ServiceStatusResponse(bool Maintenance, string? Message, string? Motd);
