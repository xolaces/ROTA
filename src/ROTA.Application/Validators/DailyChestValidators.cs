using FluentValidation;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

// System 29 — the pick index comes from the route; the controller wraps it in a request so it passes
// through FluentValidation before the service, like every other input.
public class PickDailyChestRequestValidator : AbstractValidator<PickDailyChestRequest>
{
    public PickDailyChestRequestValidator()
    {
        RuleFor(r => r.Index)
            .InclusiveBetween(0, PlayerDailyChest.ChestCount - 1)
            .WithMessage($"Pick a chest from 0 to {PlayerDailyChest.ChestCount - 1}.");
    }
}
