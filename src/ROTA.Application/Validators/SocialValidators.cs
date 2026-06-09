using FluentValidation;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public sealed class ReportPlayerRequestValidator : AbstractValidator<ReportPlayerRequest>
{
    public ReportPlayerRequestValidator(ISubjectCatalogProvider subjects)
    {
        RuleFor(x => x.Target).NotEmpty();
        // T52 — the report reason must be one of the server-catalog report subjects (key or label).
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .Must(subjects.IsValidReportSubject)
            .WithMessage("Reason must be one of the allowed report subjects.");
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Target).NotEmpty();
        RuleFor(x => x.Body).NotEmpty().WithMessage("Message body is required.").MaximumLength(2000);
    }
}
