using FluentValidation;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Validators;

// Edge validation for guild requests (System 21 Slice 1). The GuildService re-checks reserved names
// and case-insensitive uniqueness as the authoritative server gate; these catch shape/length early.

public sealed class CreateGuildRequestValidator : AbstractValidator<CreateGuildRequest>
{
    public CreateGuildRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Guild name is required.")
            .MaximumLength(32)
            .Must(n => !ReservedUsernames.IsReserved(n)).WithMessage("That guild name is reserved.");

        RuleFor(x => x.Tag)
            .NotEmpty().WithMessage("Guild tag is required.")
            .MinimumLength(2).WithMessage("Guild tag must be at least 2 characters.")
            .MaximumLength(5).WithMessage("Guild tag must be at most 5 characters.")
            .Must(t => !ReservedUsernames.IsReserved(t)).WithMessage("That guild tag is reserved.");

        RuleFor(x => x.Description).MaximumLength(500);

        RuleFor(x => x.JoinPolicy)
            .Must(BeAValidPolicy)
            .WithMessage("Join policy must be Open, Application, or InviteOnly.");
    }

    private static bool BeAValidPolicy(string? value)
        => Enum.TryParse<ROTA.Domain.Enums.GuildJoinPolicy>(value, ignoreCase: true, out _);
}

public sealed class GuildInviteRequestValidator : AbstractValidator<GuildInviteRequest>
{
    public GuildInviteRequestValidator()
    {
        RuleFor(x => x.Target).NotEmpty().WithMessage("A target player is required.");
    }
}

public sealed class UpdateGuildRequestValidator : AbstractValidator<UpdateGuildRequest>
{
    public UpdateGuildRequestValidator()
    {
        // All fields optional (null = unchanged); validate only when present.
        When(x => x.Name is not null, () =>
        {
            RuleFor(x => x.Name!)
                .NotEmpty().WithMessage("Guild name cannot be blank.")
                .MaximumLength(32)
                .Must(n => !ReservedUsernames.IsReserved(n)).WithMessage("That guild name is reserved.");
        });

        When(x => x.Tag is not null, () =>
        {
            RuleFor(x => x.Tag!)
                .MinimumLength(2).WithMessage("Guild tag must be at least 2 characters.")
                .MaximumLength(5).WithMessage("Guild tag must be at most 5 characters.")
                .Must(t => !ReservedUsernames.IsReserved(t)).WithMessage("That guild tag is reserved.");
        });

        When(x => x.Description is not null, () => RuleFor(x => x.Description!).MaximumLength(500));
        When(x => x.Motd is not null, () => RuleFor(x => x.Motd!).MaximumLength(500));

        When(x => x.JoinPolicy is not null, () =>
        {
            RuleFor(x => x.JoinPolicy!)
                .Must(v => Enum.TryParse<ROTA.Domain.Enums.GuildJoinPolicy>(v, ignoreCase: true, out _))
                .WithMessage("Join policy must be Open, Application, or InviteOnly.");
        });
    }
}
