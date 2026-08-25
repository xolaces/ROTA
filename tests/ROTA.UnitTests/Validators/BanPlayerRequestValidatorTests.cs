using FluentAssertions;
using ROTA.Application.Validators;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Validators;

/// <summary>
/// The ban request's SHAPE gate. It deliberately does not know about the northstar §6 three-day
/// moderator cap — the validator cannot see who is calling, so that rule lives in AdminService and is
/// pinned by AdminServiceTests. What this pins is that the shape gate does not accidentally enforce a
/// role rule of its own, which would refuse an admin a legitimate long ban.
/// </summary>
public class BanPlayerRequestValidatorTests
{
    private readonly BanPlayerRequestValidator _validator = new();

    [Fact]
    public void OmittedDuration_IsValid_AndMeansPermanent()
    {
        var result = _validator.Validate(new BanPlayerRequest { Reason = "cheating" });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(90)]
    [InlineData(3650)]
    public void DurationsWithinTheCeiling_AreValid(int days)
    {
        var result = _validator.Validate(new BanPlayerRequest { Reason = "cheating", DurationDays = days });
        result.IsValid.Should().BeTrue($"{days} days is a shape the service is allowed to judge");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3651)]
    public void DurationsOutsideTheCeiling_AreRejected(int days)
    {
        var result = _validator.Validate(new BanPlayerRequest { Reason = "cheating", DurationDays = days });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void BlankReason_IsRejected()
    {
        var result = _validator.Validate(new BanPlayerRequest { Reason = "  ", DurationDays = 3 });
        result.IsValid.Should().BeFalse("§6 forbids reasonless punishment");
    }
}
