using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public sealed class FeedbackRequestValidator : AbstractValidator<FeedbackRequest>
{
    private static readonly string[] Categories = { "Bug", "Feedback" };

    public FeedbackRequestValidator()
    {
        RuleFor(x => x.Category)
            .Must(c => Categories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Category must be 'Bug' or 'Feedback'.");

        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("A subject is required.")
            .MaximumLength(120);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("A description is required.")
            .MaximumLength(4000);

        RuleFor(x => x.Screen).MaximumLength(80);
    }
}
