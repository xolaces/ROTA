using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public sealed class UpdateUsernameRequestValidator : AbstractValidator<UpdateUsernameRequest>
{
    public UpdateUsernameRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .Length(3, 32)
            .Matches(@"^[a-zA-Z0-9_]+$")
            .WithMessage("Username must be 3-32 characters, alphanumeric and underscores only. No spaces or hyphens.")
            // SECURITY: prevent renaming into a reserved staff/system handle after registration.
            .Must(u => !ReservedUsernames.IsReserved(u))
            .WithMessage("That username is reserved. Please choose another.");
    }
}

public sealed class UpdateDisplayNameRequestValidator : AbstractValidator<UpdateDisplayNameRequest>
{
    public UpdateDisplayNameRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(48)
            .Matches(@"^[A-Za-z0-9_ -]+$")
            .WithMessage("DisplayName may only contain letters, digits, spaces, underscores, and hyphens.")
            // SECURITY (exploit audit 2026-06-14, finding F): block impersonating a reserved staff/system
            // handle (DEV_*, admin, owner, …). The username register/rename paths already enforce this;
            // the display-name path did not, so a player could set "DEV_Xolaces" and impersonate staff
            // across chat, leaderboards, and rosters. IsReserved is case-insensitive + prefix-aware.
            .Must(d => !ReservedUsernames.IsReserved(d))
            .WithMessage("That display name is reserved. Please choose another.");
    }
}
