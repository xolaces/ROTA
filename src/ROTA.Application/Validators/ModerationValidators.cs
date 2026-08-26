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

        // Shape only. The ROLE cap (a moderator may not exceed 3 days) lives in AdminService, which is
        // the only layer that knows who is calling. 3650 days is a decade — past that, say permanent.
        RuleFor(x => x.DurationDays)
            .InclusiveBetween(1, 3650)
            .When(x => x.DurationDays.HasValue)
            .WithMessage("A ban duration must be between 1 and 3650 days, or omitted for permanent.");
    }
}

public sealed class UnbanPlayerRequestValidator : AbstractValidator<UnbanPlayerRequest>
{
    public UnbanPlayerRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required to lift a ban.")
            .MaximumLength(500);
    }
}

public sealed class UnmutePlayerRequestValidator : AbstractValidator<UnmutePlayerRequest>
{
    public UnmutePlayerRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required to lift a mute.")
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
