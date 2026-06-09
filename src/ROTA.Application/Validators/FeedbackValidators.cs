using FluentValidation;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public sealed class FeedbackRequestValidator : AbstractValidator<FeedbackRequest>
{
    private static readonly string[] Categories = { "Bug", "Feedback" };

    public FeedbackRequestValidator(ISubjectCatalogProvider subjects)
    {
        RuleFor(x => x.Category)
            .Must(c => Categories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Category must be 'Bug' or 'Feedback'.");

        // T52 — Bug subjects must come from the server catalog (accept key or label); Feedback stays
        // open text (it is filed under the fixed feedback category on submission).
        When(x => string.Equals(x.Category, "Bug", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.Subject)
                .NotEmpty().WithMessage("A subject is required.")
                .Must(subjects.IsValidBugSubject)
                .WithMessage("Subject must be one of the allowed bug subjects.");
        });

        RuleFor(x => x.Subject).MaximumLength(120);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("A description is required.")
            .MaximumLength(4000);

        RuleFor(x => x.Screen).MaximumLength(80);
    }
}
