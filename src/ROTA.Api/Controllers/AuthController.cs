using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    // T65 anti-abuse budgets (per hour, via Redis ISubmissionRateLimiter).
    private const int ResetRequestPerEmailPerHour = 3;
    private const int ResetRequestPerIpPerHour = 10;
    private const int ResetConfirmPerEmailPerHour = 10;
    private const int ResetConfirmPerIpPerHour = 20;

    private readonly IAuthService _auth;
    private readonly ISubmissionRateLimiter _rateLimiter;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<RefreshRequest> _refreshValidator;
    private readonly IValidator<PasswordResetRequest> _resetRequestValidator;
    private readonly IValidator<ResetPasswordRequest> _resetConfirmValidator;

    public AuthController(
        IAuthService auth,
        ISubmissionRateLimiter rateLimiter,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<RefreshRequest> refreshValidator,
        IValidator<PasswordResetRequest> resetRequestValidator,
        IValidator<ResetPasswordRequest> resetConfirmValidator)
    {
        _auth = auth;
        _rateLimiter = rateLimiter;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _refreshValidator = refreshValidator;
        _resetRequestValidator = resetRequestValidator;
        _resetConfirmValidator = resetConfirmValidator;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var v = await _registerValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var result = await _auth.RegisterAsync(request, GetIpAddress());
        if (result is null)
            return Conflict(new { message = "Username or email is already in use." });

        return Created(string.Empty, result);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var v = await _loginValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var result = await _auth.LoginAsync(request, GetIpAddress());
        if (result is null)
            return Unauthorized(new { message = "Invalid credentials." });

        return Ok(result);
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var v = await _refreshValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var result = await _auth.RefreshAsync(request, GetIpAddress());
        if (result is null)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        var v = await _refreshValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        await _auth.LogoutAsync(request);
        return NoContent();
    }

    /// <summary>T65 step 1 — always 202 whether or not the email exists (anti-enumeration).</summary>
    [HttpPost("password-reset/request")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestPasswordReset([FromBody] PasswordResetRequest request)
    {
        var v = await _resetRequestValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        if (!await _rateLimiter.TryConsumeAsync("pwreset-request", EmailRateKey(request.Email),
                GetIpAddress(), ResetRequestPerEmailPerHour, ResetRequestPerIpPerHour))
            return TooManyRequests();

        await _auth.RequestPasswordResetAsync(request, GetIpAddress());
        return Accepted(new { message = "If that email is registered, a reset code is on its way." });
    }

    /// <summary>T65 step 2 — redeem the emailed code with a new password.</summary>
    [HttpPost("password-reset/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] ResetPasswordRequest request)
    {
        var v = await _resetConfirmValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        // SECURITY: the per-email budget is the code brute-force guard (~49-bit codes, 15-min TTL).
        if (!await _rateLimiter.TryConsumeAsync("pwreset-confirm", EmailRateKey(request.Email),
                GetIpAddress(), ResetConfirmPerEmailPerHour, ResetConfirmPerIpPerHour))
            return TooManyRequests();

        var ok = await _auth.ResetPasswordAsync(request, GetIpAddress());
        if (!ok)
            return UnprocessableEntity(new { message = "Invalid or expired reset code." });

        return NoContent();
    }

    // The rate limiter keys on a player Guid; pre-auth there is none, so derive a stable
    // pseudo-id from the normalized email — giving true per-email budgets.
    private static Guid EmailRateKey(string email)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }

    private IActionResult TooManyRequests()
        => StatusCode(StatusCodes.Status429TooManyRequests,
            new { message = "Too many attempts. Please try again later." });

    // SECURITY: read from connection, not X-Forwarded-For (spoofable).
    private string GetIpAddress()
        => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private IActionResult InvalidRequest(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }
}
