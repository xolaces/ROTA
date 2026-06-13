using FluentValidation;
using ROTA.Application.Services;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

// System 24 (D8) — structural validation for a battalion assignment (400 on failure). Ownership +
// UnitType band + no-duplicates are business rules resolved in the service (422).
public class SetGauntletBattalionRequestValidator : AbstractValidator<SetGauntletBattalionRequest>
{
    public SetGauntletBattalionRequestValidator()
    {
        RuleFor(x => x.Generals)
            .NotNull()
            .Must(g => g == null || g.Count <= GauntletBattalionService.GeneralSlots)
            .WithMessage($"A battalion has at most {GauntletBattalionService.GeneralSlots} generals.");

        RuleFor(x => x.Troops)
            .NotNull()
            .Must(t => t == null || t.Count <= GauntletBattalionService.TroopSlots)
            .WithMessage($"A battalion has at most {GauntletBattalionService.TroopSlots} troops.");

        RuleForEach(x => x.Generals).NotEmpty().WithMessage("General unit ids must be non-empty.");
        RuleForEach(x => x.Troops).NotEmpty().WithMessage("Troop unit ids must be non-empty.");
    }
}
