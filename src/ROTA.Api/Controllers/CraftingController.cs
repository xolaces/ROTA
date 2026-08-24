using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>System 26 — the crafting area (D-018).</summary>
[ApiController]
[Route("api/crafting")]
[Authorize]
public sealed class CraftingController : ControllerBase
{
    private readonly ICraftingService _crafting;

    public CraftingController(ICraftingService crafting)
    {
        _crafting = crafting;
    }

    /// <summary>Recipes on offer, with the caller's ingredients and blocked reasons resolved.</summary>
    [HttpGet("recipes")]
    [ProducesResponseType(typeof(CraftCatalogueResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecipes()
        => Ok(await _crafting.GetCatalogueAsync(GetPlayerId()));

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
