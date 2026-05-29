using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Admin-only REST endpoints: role management and beta key tooling.
/// All requests require the AdminOnly policy (Admin role claim OR break-glass config allowlist).
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly IBetaKeyService _betaKeys;
    private readonly IBetaKeyRepository _betaKeyRepo;
    private readonly IValidator<RoleChangeRequest> _roleValidator;
    private readonly IValidator<GenerateBetaKeysRequest> _genKeysValidator;

    public AdminController(
        IAdminService admin,
        IBetaKeyService betaKeys,
        IBetaKeyRepository betaKeyRepo,
        IValidator<RoleChangeRequest> roleValidator,
        IValidator<GenerateBetaKeysRequest> genKeysValidator)
    {
        _admin            = admin;
        _betaKeys         = betaKeys;
        _betaKeyRepo      = betaKeyRepo;
        _roleValidator    = roleValidator;
        _genKeysValidator = genKeysValidator;
    }

    /// <summary>Grants a role to a player.</summary>
    [HttpPost("players/{idOrUsername}/roles/grant")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GrantRole(
        [FromRoute] string idOrUsername,
        [FromBody] RoleChangeRequest request)
    {
        var v = await _roleValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var role = Enum.Parse<PlayerRoles>(request.Role, ignoreCase: true);
        var result = await _admin.GrantRoleAsync(GetActorId(), idOrUsername, role);

        if (!result.Success)
        {
            if (result.FailureReason?.Contains("not found") == true)
                return NotFound(new { message = result.FailureReason });
            if (result.FailureReason?.Contains("not an admin") == true)
                return Forbid();
            return BadRequest(new { message = result.FailureReason });
        }

        return Ok(new { message = "Role granted." });
    }

    /// <summary>Revokes a role from a player.</summary>
    [HttpPost("players/{idOrUsername}/roles/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeRole(
        [FromRoute] string idOrUsername,
        [FromBody] RoleChangeRequest request)
    {
        var v = await _roleValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var role = Enum.Parse<PlayerRoles>(request.Role, ignoreCase: true);
        var result = await _admin.RevokeRoleAsync(GetActorId(), idOrUsername, role);

        if (!result.Success)
        {
            if (result.FailureReason?.Contains("not found") == true)
                return NotFound(new { message = result.FailureReason });
            if (result.FailureReason?.Contains("not an admin") == true)
                return Forbid();
            return BadRequest(new { message = result.FailureReason });
        }

        return Ok(new { message = "Role revoked." });
    }

    /// <summary>Generates N beta access keys.</summary>
    [HttpPost("beta-keys")]
    [ProducesResponseType(typeof(GenerateBetaKeysResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateBetaKeys([FromBody] GenerateBetaKeysRequest request)
    {
        var v = await _genKeysValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var actorId = GetActorId();
        var keys = await _betaKeys.GenerateAsync(actorId, request.Count);
        return Ok(new GenerateBetaKeysResponse { Keys = keys.Select(k => k.Key).ToList() });
    }

    /// <summary>Lists the most-recently created beta keys (up to 200).</summary>
    [HttpGet("beta-keys")]
    [ProducesResponseType(typeof(IReadOnlyList<BetaKeyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBetaKeys()
    {
        var keys = await _betaKeyRepo.ListAsync(take: 200);
        var dtos = keys.Select(k => new BetaKeyDto
        {
            Key               = k.Key,
            IsRedeemed        = k.IsRedeemed,
            RedeemedByPlayerId = k.RedeemedByPlayerId,
            RedeemedAt        = k.RedeemedAt,
            CreatedAt         = k.CreatedAt,
        }).ToList();
        return Ok(dtos);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Guid GetActorId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private IActionResult InvalidRequest(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }
}
