using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Application.Validators;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

/// <summary>T68 — terms stamping at registration + stale-flagging at login + validator gate.</summary>
public class TermsAcceptanceAuthTests
{
    private static string Key()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static IConfiguration Config(int currentTermsVersion = 1) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "rota-test",
            ["Jwt:Audience"] = "rota-client",
            ["Jwt:PrivateKey"] = Key(),
            ["BetaGate:Enabled"] = "false",
            ["Legal:CurrentTermsVersion"] = currentTermsVersion.ToString(),
        }).Build();

    private static (AuthService service, Mock<IPlayerRepository> players, Mock<IRefreshTokenRepository> tokens)
        Build(int currentTermsVersion = 1)
    {
        var players = new Mock<IPlayerRepository>();
        var tokens = new Mock<IRefreshTokenRepository>();
        var lockout = new Mock<IAuthLockoutService>();
        lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        tokens.Setup(r => r.CountActiveSessionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var service = new AuthService(
            players.Object, tokens.Object, Config(currentTermsVersion), lockout.Object,
            new Mock<IAuditLogRepository>().Object, new Mock<IBetaKeyRepository>().Object,
            new Mock<IAchievementService>().Object,
            new Mock<IPasswordResetTokenRepository>().Object, new Mock<IEmailNotificationService>().Object);
        return (service, players, tokens);
    }

    [Fact]
    public async Task RegisterAsync_StampsAcceptedTermsVersion_AndResponseNotFlagged()
    {
        var (service, players, _) = Build();
        Player? created = null;
        players.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        players.Setup(r => r.UsernameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        players.Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .Callback((Player p, CancellationToken _) => created = p)
               .ReturnsAsync((Player p, CancellationToken _) => p);

        var result = await service.RegisterAsync(new RegisterRequest
        {
            Username = "newbie", Email = "newbie@rota.test", Password = "Secure1pass",
            AcceptedTermsVersion = 1,
        }, "127.0.0.1");

        created!.AcceptedTermsVersion.Should().Be(1);
        created.TermsAcceptedAt.Should().NotBeNull();
        result!.RequiresTermsAcceptance.Should().BeFalse();
        result.CurrentTermsVersion.Should().Be(1);
    }

    [Fact]
    public async Task LoginAsync_StaleAcceptance_FlagsRequiresTermsAcceptance()
    {
        var (service, players, _) = Build(currentTermsVersion: 2);
        var player = Player.Create("old", "old@rota.test", BCrypt.Net.BCrypt.HashPassword("Correct1", 4));
        player.AcceptTerms(1); // accepted v1, server now at v2
        players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        var result = await service.LoginAsync(
            new LoginRequest { Email = player.Email, Password = "Correct1" }, "127.0.0.1");

        result!.RequiresTermsAcceptance.Should().BeTrue();
        result.CurrentTermsVersion.Should().Be(2);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void RegisterValidator_RequiresExactCurrentTermsVersion(int accepted, bool valid)
    {
        var validator = new RegisterRequestValidator(Config(currentTermsVersion: 1));
        var result = validator.Validate(new RegisterRequest
        {
            Username = "validname", Email = "valid@rota.test", Password = "Secure1pass",
            AcceptedTermsVersion = accepted,
        });
        result.IsValid.Should().Be(valid);
    }
}
