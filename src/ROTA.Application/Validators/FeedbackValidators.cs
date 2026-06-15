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

        // SECURITY (exploit audit 2026-06-14, finding N): the game-state Snapshot was the one
        // attacker-controlled field with no bound, so it could inflate the operator-email jsonb to the
        // request-body limit. Cap entry count + per-entry key/value sizes (Subject/Description/Screen
        // are already capped above).
        RuleFor(x => x.Snapshot)
            .Must(s => s is null || s.Count <= 25)
            .WithMessage("Snapshot may contain at most 25 entries.");
        RuleForEach(x => x.Snapshot)
            .Must(kv => kv.Key.Length <= 64 && (kv.Value == null || kv.Value.Length <= 256))
            .WithMessage("Snapshot entries are too large (key ≤ 64, value ≤ 256 chars).");
    }
}
