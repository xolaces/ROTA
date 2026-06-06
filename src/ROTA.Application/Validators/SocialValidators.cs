using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public sealed class ReportPlayerRequestValidator : AbstractValidator<ReportPlayerRequest>
{
    public ReportPlayerRequestValidator()
    {
        RuleFor(x => x.Target).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("A reason is required.").MaximumLength(100);
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
