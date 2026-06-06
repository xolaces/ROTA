using FluentAssertions;
using ROTA.Application.Validators;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Validators;

public class FeedbackRequestValidatorTests
{
    private readonly FeedbackRequestValidator _validator = new();

    [Theory]
    [InlineData("Bug")]
    [InlineData("Feedback")]
    [InlineData("bug")]
    public void Accepts_ValidCategories(string category)
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = category, Subject = "s", Description = "d",
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_UnknownCategory()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Complaint", Subject = "s", Description = "d",
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_EmptyDescription()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Bug", Subject = "s", Description = "",
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_EmptySubject()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Bug", Subject = "", Description = "d",
        });
        result.IsValid.Should().BeFalse();
    }
}
