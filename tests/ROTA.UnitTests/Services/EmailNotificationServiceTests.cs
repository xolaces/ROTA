using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class EmailNotificationServiceTests
{
    private static (EmailNotificationService service,
                    Mock<IOutboundEmailRepository> emails,
                    Mock<IEmailSendQueue> queue,
                    Mock<IEmailService> email,
                    Mock<IAuditLogRepository> audit)
        BuildService(EmailConfig? cfg = null)
    {
        var emails = new Mock<IOutboundEmailRepository>();
        var queue = new Mock<IEmailSendQueue>();
        var email = new Mock<IEmailService>();
        var audit = new Mock<IAuditLogRepository>();
        var options = Options.Create(cfg ?? new EmailConfig { OperatorRecipient = "rotadevteam@gmail.com" });
        var log = new Mock<ILogger<EmailNotificationService>>();
        var service = new EmailNotificationService(
            emails.Object, queue.Object, email.Object, audit.Object, options, log.Object);
        return (service, emails, queue, email, audit);
    }

    private static EmailPayload SamplePayload() => new()
    {
        Type = EmailType.PlayerReport,
        Subject = "Player reported",
        Summary = "alice reported bob for harassment",
        TriggeringPlayerId = Guid.NewGuid(),
        TriggeringSystem = "T37",
        Detail = new Dictionary<string, object?>
        {
            ["reason"] = "harassment",
            ["reportedId"] = Guid.NewGuid().ToString(),
        },
    };

    // -----------------------------------------------------------------------
    // QueueAsync — persist first, audit, enqueue (never blocks on send)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Queue_PersistsRow_WritesAudit_AndEnqueues()
    {
        var (service, emails, queue, _, audit) = BuildService();
        OutboundEmail? captured = null;
        emails.Setup(r => r.AddAsync(It.IsAny<OutboundEmail>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundEmail, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var id = await service.QueueAsync(SamplePayload(), ipAddress: "127.0.0.1");

        captured.Should().NotBeNull();
        captured!.Id.Should().Be(id);
        captured.SendStatus.Should().Be(EmailSendStatus.Queued);
        captured.ReviewStatus.Should().Be(EmailReviewStatus.Pending);
        captured.Recipient.Should().Be("rotadevteam@gmail.com");
        captured.Subject.Should().StartWith("[ROTA][PlayerReport]");
        captured.DetailJson.Should().Contain("harassment", "the structured payload is serialized to jsonb");

        emails.Verify(r => r.AddAsync(It.IsAny<OutboundEmail>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.Enqueue(id), Times.Once);
    }

    [Fact]
    public async Task Queue_PersistsPriority_FromPayload()
    {
        var (service, emails, _, _, _) = BuildService();
        OutboundEmail? captured = null;
        emails.Setup(r => r.AddAsync(It.IsAny<OutboundEmail>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundEmail, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var payload = new EmailPayload
        {
            Type = EmailType.PlayerReport,
            Subject = "Report",
            Priority = EmailPriority.High,
            Summary = "reported",
        };

        await service.QueueAsync(payload);

        captured.Should().NotBeNull();
        captured!.Priority.Should().Be(EmailPriority.High);
    }

    [Fact]
    public async Task Queue_DefaultsPriorityToNormal_WhenPayloadOmitsIt()
    {
        var (service, emails, _, _, _) = BuildService();
        OutboundEmail? captured = null;
        emails.Setup(r => r.AddAsync(It.IsAny<OutboundEmail>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundEmail, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        await service.QueueAsync(new EmailPayload
        {
            Type = EmailType.BugReport,
            Subject = "Bug",
            Summary = "bug",
        });

        captured!.Priority.Should().Be(EmailPriority.Normal);
    }

    // -----------------------------------------------------------------------
    // ProcessSendAsync — best-effort send updates send_status
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessSend_MarksSent_OnSuccess()
    {
        var (service, emails, _, email, _) = BuildService();
        var row = OutboundEmail.Create(EmailType.BugReport, "[ROTA][BugReport] x",
            "rotadevteam@gmail.com", "bug", null, "T38", "{}", null);
        emails.Setup(r => r.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        OutboundEmail? updated = null;
        emails.Setup(r => r.UpdateAsync(It.IsAny<OutboundEmail>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundEmail, CancellationToken>((e, _) => updated = e)
            .Returns(Task.CompletedTask);
        email.Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await service.ProcessSendAsync(row.Id);

        updated.Should().NotBeNull();
        updated!.SendStatus.Should().Be(EmailSendStatus.Sent);
        updated.SendAttempts.Should().Be(1);
        email.Verify(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessSend_MarksFailed_AndSwallows_OnSendException()
    {
        var (service, emails, _, email, _) = BuildService();
        var row = OutboundEmail.Create(EmailType.ModerationAction, "[ROTA][ModerationAction] x",
            "rotadevteam@gmail.com", "mute", null, "T40", null, null);
        emails.Setup(r => r.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        OutboundEmail? updated = null;
        emails.Setup(r => r.UpdateAsync(It.IsAny<OutboundEmail>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundEmail, CancellationToken>((e, _) => updated = e)
            .Returns(Task.CompletedTask);
        email.Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var act = async () => await service.ProcessSendAsync(row.Id);

        await act.Should().NotThrowAsync("send failures must never propagate to the caller");
        updated.Should().NotBeNull();
        updated!.SendStatus.Should().Be(EmailSendStatus.Failed);
        updated.LastSendError.Should().Contain("smtp down");
    }

    [Fact]
    public async Task ProcessSend_NoOp_WhenEmailMissing()
    {
        var (service, emails, _, email, _) = BuildService();
        var missing = Guid.NewGuid();
        emails.Setup(r => r.GetByIdAsync(missing, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutboundEmail?)null);

        await service.ProcessSendAsync(missing);

        email.Verify(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        emails.Verify(r => r.UpdateAsync(It.IsAny<OutboundEmail>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
