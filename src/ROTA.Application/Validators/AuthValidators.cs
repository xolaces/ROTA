using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

// BETA - Full implementation. Runs at binding, before any service-layer code.
// SECURITY: first line of defense against malformed input. These validators do NOT
// check DB state (no async) - business-rule checks (duplicates) happen in the service.

/// <summary>
/// Validates <see cref="RegisterRequest"/> inputs before the service layer is invoked.
/// </summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(32)
            // SECURITY: restrict charset to reduce homoglyph-confusion and injection surface.
            .Matches(@"^[a-zA-Z0-9_\-]+$")
            .WithMessage("Username may only contain letters, numbers, underscores, and hyphens.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(255)
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128)
            // SECURITY: enforce complexity server-side - client cannot be trusted.
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain at least one digit.");
    }
}

/// <summary>
/// Validates <see cref="LoginRequest"/> inputs.
/// Kept intentionally minimal - specific validation would leak user existence.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}

/// <summary>
/// Validates <see cref="RefreshRequest"/> inputs.
/// </summary>
public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .MaximumLength(512);   // Base64Url 256-bit is ~43 chars; 512 gives headroom
    }
}