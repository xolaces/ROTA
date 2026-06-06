using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ROTA.Api.Controllers;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;
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
            .ReturnsAsync(new ValidationResult()); // valid

        var controller = new FeedbackController(emails.Object, limiter.Object, validator.Object);
        var claims = new[] { new Claim("sub", Guid.NewGuid().ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) },
        };
        return (controller, emails, limiter);
    }

    [Fact]
    public async Task Submit_Bug_QueuesBugReportEmail_Returns202()
    {
        var (controller, emails, _) = Build();

        var result = await controller.Submit(new FeedbackRequest
        {
            Category = "Bug", Subject = "Crash on raid", Description = "It exploded",
        });

        result.Should().BeOfType<AcceptedResult>();
        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p => p.Type == EmailType.BugReport && p.TriggeringSystem == "T38"),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_Feedback_QueuesGeneralTicketEmail()
    {
        var (controller, emails, _) = Build();

        await controller.Submit(new FeedbackRequest
        {
            Category = "Feedback", Subject = "Love the game", Description = "Great work",
        });

        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p => p.Type == EmailType.GeneralTicket),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_RateLimited_Returns429_NoEmail()
    {
        var (controller, emails, _) = Build(rateLimitAllows: false);

        var result = await controller.Submit(new FeedbackRequest
        {
            Category = "Bug", Subject = "x", Description = "y",
        });

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        emails.Verify(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "a rate-limited submission must not produce an email");
    }
}
