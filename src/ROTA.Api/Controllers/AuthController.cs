using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

// BETA - Full implementation. Controller is intentionally thin.
// All business logic lives in IAuthService. No logic here except HTTP translation.
/// <summary>
/// Handles player authentication: register, login, token refresh, logout.
/// All endpoints are public (no [Authorize]) - they exist to obtain tokens.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    /// <summary>
    /// Registers a new player account.
    /// Returns 409 if username or email is already taken.
    /// </summary>
    /// <remarks>
    /// SECURITY NOTE: The 409 response does not specify WHICH field was duplicate.
    /// Leaking that information enables account enumeration attacks.
    /// </remarks>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _auth.RegisterAsync(request, GetIpAddress());

        if (result is null)
            return Conflict(new { message = "Username or email is already in use." });

        return Created(string.Empty, result);
    }

    /// <summary>
    /// Authenticates a player and issues a token pair.
    /// Returns 401 for invalid credentials, banned account, or non-existent email.
    /// </summary>
    /// <remarks>
    /// SECURITY NOTE: 401 response is intentionally generic. Differentiating
    /// "wrong password" from "email not found" enables account enumeration.
    /// </remarks>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request, GetIpAddress());

        if (result is null)
            return Unauthorized(new { message = "Invalid credentials." });

        return Ok(result);
    }

    /// <summary>
    /// Rotates a refresh token. Issues new access + refresh token pair.
    /// The consumed refresh token is immediately invalidated.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await _auth.RefreshAsync(request, GetIpAddress());

        if (result is null)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        return Ok(result);
    }

    /// <summary>
    /// Revokes the provided refresh token, ending the session.
    /// Idempotent - calling with an already-revoked token returns 204.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        await _auth.LogoutAsync(request);
        return NoContent();
    }

    // HELPERS

    /// <summary>
    /// Returns the client IP for audit logging.
    /// SECURITY: reads the connection's RemoteIpAddress directly. Do NOT parse
    /// X-Forwarded-For by hand - that header is client-spoofable. When ROTA runs
    /// behind a reverse proxy, configure ForwardedHeadersMiddleware with a trusted
    /// proxy list in Program.cs; it populates RemoteIpAddress correctly and safely.
    /// TODO-PHASE1: add ForwardedHeaders middleware once the prod proxy is known.
    /// </summary>
    private string GetIpAddress()
        => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}