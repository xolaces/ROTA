using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

/// <summary>
/// T65 — password reset. Focus: anti-enumeration silence, single-live-code invalidation,
/// player-addressed email, single-use consume, session revocation, banned-account refusal.
/// </summary>
public class PasswordResetTests
{
    private static string GenerateTestRsaPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static IConfiguration BuildConfig()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "rota-test",
                ["Jwt:Audience"] = "rota-client",
                ["Jwt:PrivateKey"] = "PLACEHOLDER",
                ["BetaGate:Enabled"] = "false",
            })
            .Build();

    private static Player MakePlayer(bool isBanned = false)
    {
        var p = Player.Create(
            "testplayer",
            "test@rota.test",
            BCrypt.Net.BCrypt.HashPassword("Correct1", 4));
        if (isBanned) p.Ban("test");
        return p;
    }

    private sealed class Harness
    {
        public Mock<IPlayerRepository> Players { get; } = new();
        public Mock<IRefreshTokenRepository> Tokens { get; } = new();
        public Mock<IAuditLogRepository> AuditLog { get; } = new();
        public Mock<IPasswordResetTokenRepository> ResetTokens { get; } = new();
        public Mock<IEmailNotificationService> Emails { get; } = new();
        public AuthService Service { get; }

        public Harness()
        {
            var lockout = new Mock<IAuthLockoutService>();
            lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);

            Service = new AuthService(
                Players.Object, Tokens.Object, BuildConfig(), lockout.Object, AuditLog.Object,
                new Mock<IBetaKeyRepository>().Object, new Mock<IAchievementService>().Object,
                ResetTokens.Object, Emails.Object);
        }
    }

    [Fact]
    public async Task RequestPasswordResetAsync_UnknownEmail_SilentNoToken_NoEmail_Audited()
    {
        var h = new Harness();
        h.Players.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Player?)null);

        await h.Service.RequestPasswordResetAsync(
            new PasswordResetRequest { Email = "ghost@rota.test" }, "127.0.0.1");

        h.ResetTokens.Verify(r => r.CreateAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Emails.Verify(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        h.AuditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "PasswordResetRequested"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_BannedAccount_SilentNoToken()
    {
        var h = new Harness();
        var player = MakePlayer(isBanned: true);
        h.Players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(player);

        await h.Service.RequestPasswordResetAsync(
            new PasswordResetRequest { Email = player.Email }, "127.0.0.1");

        h.ResetTokens.Verify(r => r.CreateAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Emails.Verify(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_KnownEmail_InvalidatesPrior_StoresHashedToken_EmailsPlayer()
    {
        var h = new Harness();
        var player = MakePlayer();
        h.Players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(player);

        PasswordResetToken? stored = null;
        h.ResetTokens.Setup(r => r.CreateAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()))
                     .Callback((PasswordResetToken t, CancellationToken _) => stored = t)
                     .Returns(Task.CompletedTask);
        EmailPayload? queued = null;
        h.Emails.Setup(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback((EmailPayload p, string? _, CancellationToken _) => queued = p)
                .ReturnsAsync(Guid.NewGuid());

        await h.Service.RequestPasswordResetAsync(
            new PasswordResetRequest { Email = player.Email }, "127.0.0.1");

        h.ResetTokens.Verify(r => r.InvalidateActiveAsync(player.Id, It.IsAny<CancellationToken>()), Times.Once);

        stored.Should().NotBeNull();
        stored!.PlayerId.Should().Be(player.Id);
        stored.CodeHash.Should().HaveLength(64); // SHA256 hex — never the plaintext code
        stored.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(30));

        queued.Should().NotBeNull();
        queued!.Type.Should().Be(EmailType.PasswordReset);
        queued.RecipientOverride.Should().Be(player.Email, "the code goes to the PLAYER, not the operator");
        var code = queued.Detail!["code"] as string;
        code.Should().MatchRegex("^[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$");
        stored.CodeHash.Should().NotContain(code!.Replace("-", ""), "only the hash is persisted");
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidCode_ChangesPassword_RevokesAllSessions()
    {
        var h = new Harness();
        var player = MakePlayer();
        var oldHash = player.PasswordHash;
        h.Players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(player);
        h.ResetTokens.Setup(r => r.TryConsumeAsync(player.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(true);

        var ok = await h.Service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = player.Email,
            Code = "abcd-2345",
            NewPassword = "BrandNew1pass",
        }, "127.0.0.1");

        ok.Should().BeTrue();
        player.PasswordHash.Should().NotBe(oldHash);
        BCrypt.Net.BCrypt.Verify("BrandNew1pass", player.PasswordHash).Should().BeTrue();
        h.Players.Verify(r => r.UpdateAsync(player, It.IsAny<CancellationToken>()), Times.Once);
        h.Tokens.Verify(r => r.RevokeAllActiveAsync(player.Id, It.IsAny<CancellationToken>()), Times.Once);
        h.AuditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "PasswordReset"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_CodeIsCaseAndDashInsensitive()
    {
        var h = new Harness();
        var player = MakePlayer();
        h.Players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(player);

        string? consumedHash = null;
        h.ResetTokens.Setup(r => r.TryConsumeAsync(player.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Callback((Guid _, string hash, CancellationToken _) => consumedHash = hash)
                     .ReturnsAsync(true);

        await h.Service.ResetPasswordAsync(new ResetPasswordRequest
        { Email = player.Email, Code = " ab cd-23 45 ", NewPassword = "BrandNew1pass" }, "127.0.0.1");
        var messy = consumedHash;

        await h.Service.ResetPasswordAsync(new ResetPasswordRequest
        { Email = player.Email, Code = "ABCD2345", NewPassword = "BrandNew1pass" }, "127.0.0.1");

        consumedHash.Should().Be(messy, "normalization must map both spellings to the same hash");
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidOrExpiredCode_ReturnsFalse_NoChanges()
    {
        var h = new Harness();
        var player = MakePlayer();
        var oldHash = player.PasswordHash;
        h.Players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(player);
        h.ResetTokens.Setup(r => r.TryConsumeAsync(player.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(false);

        var ok = await h.Service.ResetPasswordAsync(new ResetPasswordRequest
        { Email = player.Email, Code = "WRNG-CODE", NewPassword = "BrandNew1pass" }, "127.0.0.1");

        ok.Should().BeFalse();
        player.PasswordHash.Should().Be(oldHash);
        h.Players.Verify(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_UnknownEmail_ReturnsFalse_NeverTouchesTokens()
    {
        var h = new Harness();
        h.Players.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Player?)null);

        var ok = await h.Service.ResetPasswordAsync(new ResetPasswordRequest
        { Email = "ghost@rota.test", Code = "ABCD-2345", NewPassword = "BrandNew1pass" }, "127.0.0.1");

        ok.Should().BeFalse();
        h.ResetTokens.Verify(r => r.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_BannedAccount_ReturnsFalse()
    {
        var h = new Harness();
        var player = MakePlayer(isBanned: true);
        h.Players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(player);

        var ok = await h.Service.ResetPasswordAsync(new ResetPasswordRequest
        { Email = player.Email, Code = "ABCD-2345", NewPassword = "BrandNew1pass" }, "127.0.0.1");

        ok.Should().BeFalse();
        h.ResetTokens.Verify(r => r.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
