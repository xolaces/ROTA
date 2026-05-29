using FluentValidation;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public sealed class RoleChangeRequestValidator : AbstractValidator<RoleChangeRequest>
{
    public RoleChangeRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(r => Enum.TryParse<PlayerRoles>(r, ignoreCase: true, out _))
            .WithMessage("Role must be a valid PlayerRoles value (e.g. Admin, Moderator).");
    }
}

public sealed class GenerateBetaKeysRequestValidator : AbstractValidator<GenerateBetaKeysRequest>
{
    public GenerateBetaKeysRequestValidator()
    {
        RuleFor(x => x.Count)
            .InclusiveBetween(1, 100)
            .WithMessage("Count must be between 1 and 100.");
    }
}
