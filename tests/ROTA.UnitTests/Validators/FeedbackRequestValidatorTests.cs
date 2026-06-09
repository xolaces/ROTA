using FluentAssertions;
using ROTA.Application.Validators;
using ROTA.Shared.DTOs;
using ROTA.UnitTests.TestSupport;

namespace ROTA.UnitTests.Validators;

public class FeedbackRequestValidatorTests
{
    // Uses the real shipped subjects.json so the validator's on-list checks match production content.
    private readonly FeedbackRequestValidator _validator = new(SubjectCatalogFixture.Real);

    [Fact]
    public void Accepts_Bug_WithOnListSubjectKey()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Bug", Subject = "combat_issue", Description = "d",
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Accepts_Bug_WithOnListSubjectLabel()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Bug", Subject = "Combat Issue", Description = "d",
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_Bug_WithOffListSubject()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Bug", Subject = "totally_made_up", Description = "d",
        });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("anything goes here")]
    [InlineData("")]
    public void Accepts_Feedback_WithAnyOrEmptySubject(string subject)
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Feedback", Subject = subject, Description = "d",
        });
        result.IsValid.Should().BeTrue("Feedback subjects stay open text");
    }

    [Fact]
    public void Rejects_UnknownCategory()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Complaint", Subject = "Combat Issue", Description = "d",
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_EmptyDescription()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Bug", Subject = "Combat Issue", Description = "",
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_Bug_WithEmptySubject()
    {
        var result = _validator.Validate(new FeedbackRequest
        {
            Category = "Bug", Subject = "", Description = "d",
        });
        result.IsValid.Should().BeFalse();
    }
}
