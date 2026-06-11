using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;

namespace ROTA.UnitTests.Services;

/// <summary>T68 — terms/privacy acceptance.</summary>
public class LegalServiceTests
{
    private sealed class FakeTexts : ILegalTextProvider
    {
        public string TermsMarkdown => "# Terms v-test";
        public string PrivacyMarkdown => "# Privacy v-test";
    }

    private static (LegalService service, Mock<IPlayerRepository> players, Mock<IAuditLogRepository> audit)
        Build(int currentVersion = 2)
    {
        var players = new Mock<IPlayerRepository>();
        var audit = new Mock<IAuditLogRepository>();
        var service = new LegalService(
            new FakeTexts(), players.Object, audit.Object,
            Options.Create(new LegalConfig { CurrentTermsVersion = currentVersion }));
        return (service, players, audit);
    }

    private static Player MakePlayer() => Player.Create("legaltester", "legal@rota.test", "hash");

    [Fact]
    public void GetTerms_ReturnsMarkdownAndCurrentVersion()
    {
        var (service, _, _) = Build(currentVersion: 3);
        var doc = service.GetTerms();
        doc.Document.Should().Be("terms");
        doc.Version.Should().Be(3);
        doc.Markdown.Should().Contain("Terms");
    }

    [Fact]
    public async Task AcceptTermsAsync_CurrentVersion_StampsPlayer_Audits()
    {
        var (service, players, audit) = Build(currentVersion: 2);
        var player = MakePlayer();
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        var status = await service.AcceptTermsAsync(player.Id, 2, "127.0.0.1");

        status.Should().Be(AcceptTermsStatus.Success);
        player.AcceptedTermsVersion.Should().Be(2);
        player.TermsAcceptedAt.Should().NotBeNull();
        players.Verify(r => r.UpdateAsync(player, It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "TermsAccepted"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptTermsAsync_StaleVersion_Rejected_NoWrite()
    {
        var (service, players, _) = Build(currentVersion: 2);

        var status = await service.AcceptTermsAsync(Guid.NewGuid(), 1, "127.0.0.1");

        status.Should().Be(AcceptTermsStatus.StaleVersion);
        players.Verify(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcceptTermsAsync_AlreadyAccepted_IdempotentSuccess_NoSecondWrite()
    {
        var (service, players, _) = Build(currentVersion: 2);
        var player = MakePlayer();
        player.AcceptTerms(2);
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        var status = await service.AcceptTermsAsync(player.Id, 2, "127.0.0.1");

        status.Should().Be(AcceptTermsStatus.Success);
        players.Verify(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcceptTermsAsync_UnknownPlayer_NotFound()
    {
        var (service, players, _) = Build();
        players.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);

        var status = await service.AcceptTermsAsync(Guid.NewGuid(), 2, "127.0.0.1");

        status.Should().Be(AcceptTermsStatus.NotFound);
    }

    [Fact]
    public void Player_AcceptTerms_NeverDowngrades()
    {
        var player = MakePlayer();
        player.AcceptTerms(3);
        player.AcceptTerms(2);
        player.AcceptedTermsVersion.Should().Be(3);
    }
}
