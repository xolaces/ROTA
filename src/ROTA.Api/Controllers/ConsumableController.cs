using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// D-008 / D-013 — gem-priced instant refills. Gold-priced potions are inventory items and live on
/// <see cref="ItemController"/> (<c>/api/items/shop</c>); this is the premium, no-inventory tier.
/// </summary>
[ApiController]
[Route("api/consumables")]
[Authorize]
public sealed class ConsumableController : ControllerBase
{
    private readonly IConsumableService _consumables;

    public ConsumableController(IConsumableService consumables)
    {
        _consumables = consumables;
    }

    /// <summary>Refillable pools with their gem price and the caller's current state.</summary>
    [HttpGet("refills")]
    [ProducesResponseType(typeof(RefillOptionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRefillOptions()
        => Ok(await _consumables.GetRefillOptionsAsync(GetPlayerId()));

    /// <summary>Spends gems to fill a pool to maximum.</summary>
    [HttpPost("refills/{resourceType}")]
    [ProducesResponseType(typeof(RefillResourceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RefillResourceResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RefillResourceResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RefillResourceResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(RefillResourceResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Refill([FromRoute] string resourceType)
    {
        if (!Enum.TryParse<ResourceType>(resourceType, ignoreCase: true, out var parsed))
            return NotFound(new RefillResourceResponse
            {
                FailureCode   = RefillFailureCode.ResourceNotFound,
                FailureReason = $"'{resourceType}' is not a resource.",
            });

        var result = await _consumables.RefillAsync(GetPlayerId(), parsed);
        if (result.Success) return Ok(result);

        return result.FailureCode switch
        {
            RefillFailureCode.ResourceNotFound => NotFound(result),
            RefillFailureCode.NotRefillable    => BadRequest(result),
            // Already full is a state conflict, not a bad request — the client should re-read and stop offering it.
            RefillFailureCode.AlreadyFull      => Conflict(result),
            RefillFailureCode.InsufficientGems => UnprocessableEntity(result),
            _                                  => BadRequest(result),
        };
    }

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
