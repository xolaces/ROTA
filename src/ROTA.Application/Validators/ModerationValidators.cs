using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public sealed class BanPlayerRequestValidator : AbstractValidator<BanPlayerRequest>
{
    public BanPlayerRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A ban reason is required.")
            .MaximumLength(500);
    }
}

public sealed class MutePlayerRequestValidator : AbstractValidator<MutePlayerRequest>
{
    // Cap the mute at 30 days so a typo can't mute a player effectively forever.
    private const int MaxMinutes = 60 * 24 * 30;

    public MutePlayerRequestValidator()
    {
        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("Mute duration must be positive.")
            .LessThanOrEqualTo(MaxMinutes).WithMessage("Mute duration cannot exceed 30 days.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A mute reason is required.")
            .MaximumLength(500);
    }
}
