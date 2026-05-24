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
            .WithMessage("Username must be 3-32 characters, alphanumeric and underscores only. No spaces or hyphens.");
    }
}
