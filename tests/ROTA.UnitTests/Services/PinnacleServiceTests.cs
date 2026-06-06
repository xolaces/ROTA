using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class PinnacleServiceTests
{
    private static (PinnacleService service,
                    Mock<IPinnacleClaimRepository> claims,
                    Mock<IEmailNotificationService> emails,
                    Mock<IAuditLogRepository> audit)
        Build()
    {
        var claims = new Mock<IPinnacleClaimRepository>();
        var emails = new Mock<IEmailNotificationService>();
        var audit  = new Mock<IAuditLogRepository>();
        return (new PinnacleService(claims.Object, emails.Object, audit.Object), claims, emails, audit);
    }

    [Fact]
    public async Task RecordFirstClaim_FirstClaimer_AuditsAndEmails()
    {
        var (service, claims, emails, audit) = Build();
        var playerId = Guid.NewGuid();
        claims.Setup(c => c.TryClaimAsync(5000, playerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await service.RecordFirstClaimAsync(playerId, 5000);

        result.Should().BeTrue();
        audit.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "PinnacleFirstClaim"), It.IsAny<CancellationToken>()), Times.Once);
        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p => p.Type == EmailType.PinnacleFirstClaim),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFirstClaim_AlreadyClaimed_NoEmailNoAudit()
    {
        var (service, claims, emails, audit) = Build();
        var playerId = Guid.NewGuid();
        claims.Setup(c => c.TryClaimAsync(5000, playerId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await service.RecordFirstClaimAsync(playerId, 5000);

        result.Should().BeFalse("the level was already claimed by someone else");
        emails.Verify(e => e.QueueAsync(
            It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
