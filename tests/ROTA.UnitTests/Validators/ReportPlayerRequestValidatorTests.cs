using FluentAssertions;
using ROTA.Application.Validators;
using ROTA.Shared.DTOs;
using ROTA.UnitTests.TestSupport;

namespace ROTA.UnitTests.Validators;

public class ReportPlayerRequestValidatorTests
{
    private readonly ReportPlayerRequestValidator _validator = new(SubjectCatalogFixture.Real);

    [Theory]
    [InlineData("cheating")]
    [InlineData("Cheating")]        // label form accepted too
    [InlineData("harassment")]
    public void Accepts_OnListReason(string reason)
    {
        var result = _validator.Validate(new ReportPlayerRequest { Target = "bob", Reason = reason });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("not_a_reason")]
    [InlineData("")]
    public void Rejects_OffListReason(string reason)
    {
        var result = _validator.Validate(new ReportPlayerRequest { Target = "bob", Reason = reason });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_EmptyTarget()
    {
        var result = _validator.Validate(new ReportPlayerRequest { Target = "", Reason = "cheating" });
        result.IsValid.Should().BeFalse();
    }
}
