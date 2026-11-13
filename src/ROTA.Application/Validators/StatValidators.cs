using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

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

        // triage stat-alloc-validator-cap: the old <=100 placeholder 400'd any bulk allocation. The
        // service already gates by available SkillPoints + LSI, so this is just a sane overflow guard.
        RuleFor(r => r.Amount)
            .GreaterThanOrEqualTo(1).WithMessage("Amount must be at least 1.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Amount cannot exceed 100,000,000.");
    }
}

public class AllocateStatsRequestValidator : AbstractValidator<AllocateStatsRequest>
{
    public AllocateStatsRequestValidator()
    {
        RuleFor(r => r.Allocations)
            .NotEmpty().WithMessage("Nothing to allocate.")
            .Must(a => a == null || a.Count <= 6).WithMessage("At most one entry per stat.")
            .Must(a => a == null || a.Select(x => (x.StatType ?? "").ToLowerInvariant()).Distinct().Count() == a.Count)
            .WithMessage("Each stat may appear once.");
        RuleForEach(r => r.Allocations).SetValidator(new AllocateStatRequestValidator());
    }
}
