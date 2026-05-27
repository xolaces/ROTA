using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

// BETA — validates stat allocation requests before they reach StatService.
public class AllocateStatRequestValidator : AbstractValidator<AllocateStatRequest>
{
    private static readonly HashSet<string> ValidStatTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Energy", "Stamina", "Attack", "Defense", "Health", "Discernment"
    };

    public AllocateStatRequestValidator()
    {
        RuleFor(r => r.StatType)
            .NotEmpty().WithMessage("StatType is required.")
            .Must(s => ValidStatTypes.Contains(s))
            .WithMessage("StatType must be one of: Energy, Stamina, Attack, Defense, Health, Discernment.");

        RuleFor(r => r.Amount)
            .GreaterThanOrEqualTo(1).WithMessage("Amount must be at least 1.")
            .LessThanOrEqualTo(100).WithMessage("Amount cannot exceed 100.");
    }
}
