using FluentValidation;
using Microsoft.Extensions.Configuration;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

// SECURITY: first line of defense against malformed input. These validators do NOT
// check DB state (no async) - business-rule checks (duplicates) happen in the service.

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator(IConfiguration config)
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(32)
            // SECURITY: restrict charset to reduce homoglyph-confusion and injection surface.
            .Matches(@"^[a-zA-Z0-9_\-]+$")
            .WithMessage("Username may only contain letters, numbers, underscores, and hyphens.")
            // SECURITY: staff/system handles are reserved — created only via admin CLI / seeding.
            .Must(u => !ReservedUsernames.IsReserved(u))
            .WithMessage("That username is reserved. Please choose another.");

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

        // T68 — registration requires accepting the CURRENT terms version exactly.
        var currentTermsVersion = config.GetValue("Legal:CurrentTermsVersion", 1);
        RuleFor(x => x.AcceptedTermsVersion)
            .Equal(currentTermsVersion)
            .WithMessage("You must accept the current Terms of Service and Privacy Policy to register.");

        // BetaKey is required when the beta gate is enabled (default: true).
        var betaGateEnabled = config.GetValue("BetaGate:Enabled", true);
        if (betaGateEnabled)
        {
            RuleFor(x => x.BetaKey)
                .NotEmpty()
                .WithMessage("A beta access key is required for registration.")
                .MaximumLength(20)
                .WithMessage("Beta key must be in ROTA-XXXX-XXXX-XXXX format.");
        }
    }
}

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

public sealed class PasswordResetRequestValidator : AbstractValidator<PasswordResetRequest>
{
    public PasswordResetRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(255)
            .EmailAddress();
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(255)
            .EmailAddress();

        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(16); // XXXX-XXXX plus headroom for stray separators

        // Same complexity bar as registration — a reset must never weaken the account.
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128)
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain at least one digit.");
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .MaximumLength(512);   // Base64Url 256-bit is ~43 chars; 512 gives headroom
    }
}