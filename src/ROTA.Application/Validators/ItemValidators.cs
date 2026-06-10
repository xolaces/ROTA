using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

public class UseItemRequestValidator : AbstractValidator<UseItemRequest>
{
    public UseItemRequestValidator()
    {
        RuleFor(r => r.Quantity)
            .GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.")
            .LessThanOrEqualTo(1000).WithMessage("Quantity cannot exceed 1000.");
    }
}
