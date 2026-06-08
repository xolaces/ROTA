using FluentValidation;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public sealed class PledgeRequestValidator : AbstractValidator<PledgeRequest>
{
    public PledgeRequestValidator()
    {
        RuleFor(x => x.Ancient)
            .NotEmpty().WithMessage("Ancient is required.")
            .Must(a => Enum.TryParse<MasteryAncient>(a, ignoreCase: true, out _))
            .WithMessage("Ancient must be one of: Wrath, Bulwark, Hoard, Discernment.");
    }
}
