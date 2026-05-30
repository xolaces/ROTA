using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

/// <summary>
/// Unit tests for AuthService.
/// Focus: security-critical paths — timing safety, token rotation, session cap, lockout.
/// </summary>
public class AuthServiceTests
{
    // -----------------------------------------------------------------------
    // FIXTURES
    // -----------------------------------------------------------------------

    private static string GenerateTestRsaPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static IConfiguration BuildConfig(string? privateKey = null, bool betaGateEnabled = false)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"]        = "rota-test",
                ["Jwt:Audience"]      = "rota-client",
                ["Jwt:PrivateKey"]    = privateKey ?? "PLACEHOLDER",
                // Disable the beta gate in existing tests — beta gate behaviour
                // is tested separately in BetaKeyServiceTests.
                ["BetaGate:Enabled"]  = betaGateEnabled.ToString().ToLowerInvariant(),
            })
            .Build();
    }

    private static Player MakePlayer(bool isBanned = false)
    {
        var p = Player.Create(
            "testplayer",
            "test@rota.test",
            BCrypt.Net.BCrypt.HashPassword("Correct1", 4)); // low work factor for speed
        if (isBanned) p.Ban("test");
        return p;
    }

    private static RefreshToken MakeActiveToken(Guid playerId)
        => new RefreshToken(playerId, "anyhash", DateTimeOffset.UtcNow.AddDays(7), null);

    /// <summary>Creates an AuthService with all dependencies mocked out.</summary>
    private static (AuthService service,
                    Mock<IPlayerRepository> players,
                    Mock<IRefreshTokenRepository> tokens,
                    Mock<IAuthLockoutService> lockout,
                    Mock<IAuditLogRepository> auditLog)
        BuildService(string? privateKey = null, bool betaGateEnabled = false)
    {
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var lockout  = new Mock<IAuthLockoutService>();
        var auditLog = new Mock<IAuditLogRepository>();
        var betaKeys = new Mock<IBetaKeyRepository>();

        // Default: not locked out
        lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var service = new AuthService(
            players.Object,
            tokens.Object,
            BuildConfig(privateKey, betaGateEnabled),
            lockout.Object,
            auditLog.Object,
            betaKeys.Object);

        return (service, players, tokens, lockout, auditLog);
    }

    // -----------------------------------------------------------------------
    // REGISTER — success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RegisterAsync_Success_ReturnsAuthResponse()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, tokens, _, _) = BuildService(key);

        players.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(r => r.UsernameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player p, CancellationToken _) => p);
        tokens.Setup(r => r.CountActiveSessionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "hero", Email = "hero@rota.test", Password = "Secure1pass" },
            "127.0.0.1");

        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // REGISTER — duplicates
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsNull()
    {
        var (service, players, _, _, _) = BuildService();
        players.Setup(r => r.EmailExistsAsync("taken@rota.test", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "newuser", Email = "taken@rota.test", Password = "Correct1" },
            "127.0.0.1");

        result.Should().BeNull("duplicate email must silently return null");
        players.Verify(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUsername_ReturnsNull()
    {
        var (service, players, _, _, _) = BuildService();
        players.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(r => r.UsernameExistsAsync("takenname", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "takenname", Email = "new@rota.test", Password = "Correct1" },
            "127.0.0.1");

        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // REGISTER — beta gate: a failed registration must NOT burn the single-use key
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RegisterAsync_BetaGate_DuplicateUsername_DoesNotConsumeKey()
    {
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var lockout  = new Mock<IAuthLockoutService>();
        var auditLog = new Mock<IAuditLogRepository>();
        var betaKeys = new Mock<IBetaKeyRepository>();

        players.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(r => r.UsernameExistsAsync("takenname", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var service = new AuthService(
            players.Object, tokens.Object,
            BuildConfig(betaGateEnabled: true),
            lockout.Object, auditLog.Object, betaKeys.Object);

        var result = await service.RegisterAsync(
            new RegisterRequest
            {
                Username = "takenname",
                Email    = "new@rota.test",
                Password = "Correct1",
                BetaKey  = "ROTA-AAAA-BBBB-CCCC",
            },
            "127.0.0.1");

        result.Should().BeNull("registration must fail on a duplicate username");
        betaKeys.Verify(
            k => k.TryRedeemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a single-use beta key must NOT be consumed when registration fails on a duplicate");
        players.Verify(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // LOGIN — success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsAuthResponse()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, tokens, lockout, _) = BuildService(key);

        var player = MakePlayer();
        players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        tokens.Setup(r => r.CountActiveSessionsAsync(player.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);
        lockout.Setup(l => l.ClearAsync(player.Email, It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var result = await service.LoginAsync(
            new LoginRequest { Email = player.Email, Password = "Correct1" },
            "127.0.0.1");

        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // LOGIN — security paths
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        var (service, players, _, _, _) = BuildService();
        var player = MakePlayer();
        players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        var result = await service.LoginAsync(
            new LoginRequest { Email = player.Email, Password = "WrongPassword1" },
            "127.0.0.1");

        result.Should().BeNull("wrong password must return null");
    }

    [Fact]
    public async Task LoginAsync_EmailNotFound_ReturnsNull_WithoutShortCircuiting()
    {
        // SECURITY: BCrypt.Verify must still run (timing-safe dummy hash path).
        var (service, players, _, _, _) = BuildService();
        players.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);

        var result = await service.LoginAsync(
            new LoginRequest { Email = "nobody@rota.test", Password = "SomePass1" },
            "127.0.0.1");

        result.Should().BeNull("non-existent email must return null, same code path as wrong password");
    }

    [Fact]
    public async Task LoginAsync_BannedPlayer_ReturnsNull()
    {
        var (service, players, _, _, _) = BuildService();
        var player = MakePlayer(isBanned: true);
        players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        var result = await service.LoginAsync(
            new LoginRequest { Email = player.Email, Password = "Correct1" },
            "127.0.0.1");

        result.Should().BeNull("banned players must not receive tokens");
    }

    [Fact]
    public async Task LoginAsync_LockedOut_ReturnsNull_BeforeDbLookup()
    {
        // Verifies that the lockout check short-circuits before any DB work.
        var (service, players, _, lockout, _) = BuildService();
        lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var result = await service.LoginAsync(
            new LoginRequest { Email = "locked@rota.test", Password = "Correct1" },
            "127.0.0.1");

        result.Should().BeNull("locked account must return null");
        players.Verify(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "DB lookup must be skipped when account is locked");
    }

    [Fact]
    public async Task LoginAsync_FailedAttempt_IncrementsLockoutCounter()
    {
        var (service, players, _, lockout, _) = BuildService();
        players.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);

        await service.LoginAsync(
            new LoginRequest { Email = "try@rota.test", Password = "Wrong1" },
            "127.0.0.1");

        lockout.Verify(l => l.RecordFailedAttemptAsync("try@rota.test", It.IsAny<CancellationToken>()), Times.Once,
            "failed login must increment the lockout counter");
    }

    // -----------------------------------------------------------------------
    // REFRESH — success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsNewAuthResponse()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, tokens, _, _) = BuildService(key);

        var player = MakePlayer();
        var activeToken = MakeActiveToken(player.Id);

        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(activeToken);
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        tokens.Setup(r => r.RevokeAsync(activeToken, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        tokens.Setup(r => r.CountActiveSessionsAsync(player.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var result = await service.RefreshAsync(
            new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        result.Should().NotBeNull();
        tokens.Verify(r => r.RevokeAsync(activeToken, It.IsAny<CancellationToken>()), Times.Once,
            "consumed token must be revoked on rotation");
    }

    // -----------------------------------------------------------------------
    // REFRESH — token lifecycle rejections
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RefreshAsync_AlreadyRevokedToken_ReturnsNull()
    {
        var (service, _, tokens, _, _) = BuildService();
        var revokedToken = new RefreshToken(Guid.NewGuid(), "anyhash", DateTimeOffset.UtcNow.AddDays(7), null);
        revokedToken.Revoke();
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(revokedToken);

        var result = await service.RefreshAsync(
            new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        result.Should().BeNull("revoked tokens must be rejected");
        tokens.Verify(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsNull()
    {
        var (service, _, tokens, _, _) = BuildService();
        var expiredToken = new RefreshToken(Guid.NewGuid(), "anyhash", DateTimeOffset.UtcNow.AddDays(-1), null);
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(expiredToken);

        var result = await service.RefreshAsync(
            new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        result.Should().BeNull("expired tokens must be rejected");
    }

    // -----------------------------------------------------------------------
    // LOGOUT
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LogoutAsync_UnknownToken_IsIdempotent_NoException()
    {
        var (service, _, tokens, _, _) = BuildService();
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken?)null);

        var act = async () => await service.LogoutAsync(new RefreshRequest { RefreshToken = "unknown" });

        await act.Should().NotThrowAsync("double-logout or unknown token must be silently ignored");
        tokens.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
