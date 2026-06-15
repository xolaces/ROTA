using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ROTA.Api.Controllers;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Validators;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;
using ROTA.UnitTests.TestSupport;
using System.Security.Claims;

namespace ROTA.UnitTests.Controllers;

public class FeedbackControllerTests
{
    private static (FeedbackController controller,
                    Mock<IEmailNotificationService> emails,
                    Mock<ISubmissionRateLimiter> limiter)
        Build(bool rateLimitAllows = true)
    {
        var emails = new Mock<IEmailNotificationService>();
        emails.Setup(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var limiter = new Mock<ISubmissionRateLimiter>();
        limiter.Setup(l => l.TryConsumeAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rateLimitAllows);

        var validator = new Mock<IValidator<FeedbackRequest>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<FeedbackRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var controller = new FeedbackController(
            emails.Object, limiter.Object, validator.Object, SubjectCatalogFixture.Real);
        var claims = new[] { new Claim("sub", Guid.NewGuid().ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) },
        };
        return (controller, emails, limiter);
    }

    [Fact]
    public async Task Submit_Bug_QueuesBugReportEmail_NormalPriority_Returns202()
    {
        var (controller, emails, _) = Build();

        var result = await controller.Submit(new FeedbackRequest
        {
            Category = "Bug", Subject = "combat_issue", Description = "It exploded",
        });

        result.Should().BeOfType<AcceptedResult>();
        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p =>
                p.Type == EmailType.BugReport &&
                p.TriggeringSystem == "T38" &&
                p.Priority == EmailPriority.Normal &&
                p.Subject == "Combat Issue"),  // normalized key → label
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_Feedback_QueuesGeneralTicket_LowPriority_FixedCategorySubject()
    {
        var (controller, emails, _) = Build();

        await controller.Submit(new FeedbackRequest
        {
            Category = "Feedback", Subject = "Love the game", Description = "Great work",
        });

        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p =>
                p.Type == EmailType.GeneralTicket &&
                p.Priority == EmailPriority.Low &&
                p.Subject == "Player Feedback"),  // open-text subject overridden by the fixed category
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_OffListBugSubject_Returns400_NoEmail()
    {
        var (controller, emails, _) = Build();

        // Use the REAL validator here so an off-list subject is actually rejected.
        var realValidator = new FeedbackRequestValidator(SubjectCatalogFixture.Real);
        var wired = new FeedbackController(
            emails.Object,
            Mock.Of<ISubmissionRateLimiter>(l => l.TryConsumeAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)),
            realValidator,
            SubjectCatalogFixture.Real);
        wired.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) })),
            },
        };

        var result = await wired.Submit(new FeedbackRequest
        {
            Category = "Bug", Subject = "not_a_real_subject", Description = "x",
        });

        // ValidationProblem() returns an ObjectResult carrying ValidationProblemDetails — the 400 status
        // is applied by the MVC pipeline (absent in this bare unit test), so we assert the validation-
        // failure shape and the subject error, which is what the 400 carries to the client.
        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        var details = obj.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        details.Errors.Should().ContainKey(nameof(FeedbackRequest.Subject));
        emails.Verify(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "an off-list bug subject must be rejected before any email is queued");
    }

    [Fact]
    public async Task Submit_RateLimited_Returns429_NoEmail()
    {
        var (controller, emails, _) = Build(rateLimitAllows: false);

        var result = await controller.Submit(new FeedbackRequest
        {
            Category = "Bug", Subject = "combat_issue", Description = "y",
        });

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        emails.Verify(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "a rate-limited submission must not produce an email");
    }
}
