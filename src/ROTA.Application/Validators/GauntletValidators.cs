using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

// BETA (System 16 Slice 2) — request validators run before the service layer (architecture rule).

public sealed class BuyStrikesRequestValidator : AbstractValidator<BuyStrikesRequest>
{
    public BuyStrikesRequestValidator()
    {
        RuleFor(x => x.Strikes)
            .GreaterThan(0).WithMessage("Strikes to buy must be greater than zero.");

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty().WithMessage("An idempotency key is required.")
            .MaximumLength(100);
    }
}

public sealed class OpenGauntletEventRequestValidator : AbstractValidator<OpenGauntletEventRequest>
{
    public OpenGauntletEventRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("An event name is required.")
            .MaximumLength(100);

        RuleFor(x => x.EndsAt)
            .GreaterThan(x => x.StartsAt).WithMessage("endsAt must be after startsAt.");
    }
}
