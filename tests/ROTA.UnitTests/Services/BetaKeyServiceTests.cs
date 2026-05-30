using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;
using System.Security.Cryptography;

namespace ROTA.UnitTests.Services;

/// <summary>
/// Unit tests for BetaKeyService and the beta-gated registration path in AuthService.
/// </summary>
public class BetaKeyServiceTests
{
    // -----------------------------------------------------------------------
    // FIXTURES
    // -----------------------------------------------------------------------

    private static BetaKeyService BuildBetaKeyService(
        out Mock<IBetaKeyRepository> betaKeysMock,
        out Mock<IAuditLogRepository> auditMock)
    {
        betaKeysMock = new Mock<IBetaKeyRepository>();
        auditMock    = new Mock<IAuditLogRepository>();
        return new BetaKeyService(betaKeysMock.Object, auditMock.Object);
    }

    private static string GenerateTestRsaPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static IConfiguration BuildConfig(string? privateKey = null, bool betaGateEnabled = true)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"]       = "rota-test",
                ["Jwt:Audience"]     = "rota-client",
                ["Jwt:PrivateKey"]   = privateKey ?? "PLACEHOLDER",
                ["BetaGate:Enabled"] = betaGateEnabled.ToString().ToLowerInvariant(),
            })
            .Build();

    /// <summary>Builds AuthService with beta gate enabled or disabled.</summary>
    private static (AuthService service,
                    Mock<IPlayerRepository> players,
                    Mock<IRefreshTokenRepository> tokens,
                    Mock<IBetaKeyRepository> betaKeys,
                    Mock<IAuditLogRepository> auditLog)
        BuildAuthService(bool betaGateEnabled = true, string? privateKey = null)
    {
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var lockout  = new Mock<IAuthLockoutService>();
        var auditLog = new Mock<IAuditLogRepository>();
        var betaKeys = new Mock<IBetaKeyRepository>();

        lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        tokens.Setup(r => r.CountActiveSessionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var service = new AuthService(
            players.Object, tokens.Object,
            BuildConfig(privateKey, betaGateEnabled),
            lockout.Object, auditLog.Object, betaKeys.Object);

        return (service, players, tokens, betaKeys, auditLog);
    }

    // -----------------------------------------------------------------------
    // BetaKeyService.GenerateAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_ReturnsCorrectCount_AndFormatsKeys()
    {
        var svc = BuildBetaKeyService(out var betaKeysMock, out _);
        betaKeysMock
            .Setup(r => r.CreateAsync(It.IsAny<BetaKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BetaKey k, CancellationToken _) => k);

        var keys = await svc.GenerateAsync(actorPlayerId: null, count: 3);

        keys.Should().HaveCount(3);
        foreach (var k in keys)
        {
            k.Key.Should().MatchRegex(@"^ROTA-[2-9A-HJ-NP-TV-Z]{4}-[2-9A-HJ-NP-TV-Z]{4}-[2-9A-HJ-NP-TV-Z]{4}$",
                "keys must use Crockford base32 with no 0/O/1/I/L characters");
            k.IsRedeemed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task GenerateAsync_WritesAuditLog()
    {
        var svc = BuildBetaKeyService(out var betaKeysMock, out var auditMock);
        betaKeysMock
            .Setup(r => r.CreateAsync(It.IsAny<BetaKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BetaKey k, CancellationToken _) => k);

        await svc.GenerateAsync(actorPlayerId: Guid.NewGuid(), count: 1);

        auditMock.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "BetaKeyGenerated"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_CliActor_GuidEmpty_NullCreatedBy()
    {
        var svc = BuildBetaKeyService(out var betaKeysMock, out _);
        BetaKey? captured = null;
        betaKeysMock
            .Setup(r => r.CreateAsync(It.IsAny<BetaKey>(), It.IsAny<CancellationToken>()))
            .Callback<BetaKey, CancellationToken>((k, _) => captured = k)
            .ReturnsAsync((BetaKey k, CancellationToken _) => k);

        await svc.GenerateAsync(actorPlayerId: Guid.Empty, count: 1);

        captured.Should().NotBeNull();
        captured!.CreatedByPlayerId.Should().BeNull("Guid.Empty actor is the CLI — CreatedByPlayerId must be null");
    }

    // -----------------------------------------------------------------------
    // BetaKeyService.ValidateAndRedeemAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndRedeemAsync_ValidKey_ReturnsTrue()
    {
        var svc = BuildBetaKeyService(out var betaKeysMock, out _);
        betaKeysMock
            .Setup(r => r.TryRedeemAsync("ROTA-TEST-KEY-1234", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await svc.ValidateAndRedeemAsync("ROTA-TEST-KEY-1234", Guid.NewGuid());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAndRedeemAsync_AlreadyRedeemed_ReturnsFalse()
    {
        var svc = BuildBetaKeyService(out var betaKeysMock, out _);
        betaKeysMock
            .Setup(r => r.TryRedeemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await svc.ValidateAndRedeemAsync("ROTA-USED-KEY-XXXX", Guid.NewGuid());

        result.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // AuthService.RegisterAsync — beta gate path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RegisterAsync_BetaGateEnabled_InvalidKey_ReturnsNull_NoPlayerCreated()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, _, betaKeys, auditLog) = BuildAuthService(betaGateEnabled: true, privateKey: key);

        // WithTransactionAsync runs the work without a real DB transaction in unit tests.
        betaKeys
            .Setup(r => r.WithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<AuthResponse?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<AuthResponse?>>, CancellationToken>(
                (work, ct) => work(ct));

        // TryRedeemAsync returns false — key is invalid or already used.
        betaKeys
            .Setup(r => r.TryRedeemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "hacker", Email = "h@rota.test", Password = "Secure1Pass", BetaKey = "ROTA-FAKE-FAKE-FAKE" },
            "127.0.0.1");

        result.Should().BeNull("invalid beta key must block registration");
        players.Verify(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never,
            "player must NOT be created when the beta key is invalid");
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "RegisterFailed"),
            It.IsAny<CancellationToken>()), Times.Once,
            "a RegisterFailed audit entry must be written");
    }

    [Fact]
    public async Task RegisterAsync_BetaGateEnabled_ValidKey_ReturnsAuthResponse()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, tokens, betaKeys, _) = BuildAuthService(betaGateEnabled: true, privateKey: key);

        betaKeys
            .Setup(r => r.WithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<AuthResponse?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<AuthResponse?>>, CancellationToken>(
                (work, ct) => work(ct));

        betaKeys
            .Setup(r => r.TryRedeemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        players.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(r => r.UsernameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player p, CancellationToken _) => p);

        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "newplayer", Email = "new@rota.test", Password = "Secure1Pass", BetaKey = "ROTA-GOOD-KEY-1234" },
            "127.0.0.1");

        result.Should().NotBeNull("valid beta key must allow registration");
        result!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RegisterAsync_BetaGateEnabled_DoubleRedeem_SecondCallReturnsFalse()
    {
        // Simulates the race: first call returns true (wins), second returns false (loses).
        var key = GenerateTestRsaPrivateKey();

        bool firstCall = true;

        var (svc1, players1, tokens1, betaKeys1, _) = BuildAuthService(betaGateEnabled: true, privateKey: key);
        var (svc2, _, _, betaKeys2, _) = BuildAuthService(betaGateEnabled: true);

        foreach (var bk in new[] { betaKeys1, betaKeys2 })
        {
            bk.Setup(r => r.WithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<AuthResponse?>>>(),
                    It.IsAny<CancellationToken>()))
              .Returns<Func<CancellationToken, Task<AuthResponse?>>, CancellationToken>(
                  (work, ct) => work(ct));
        }

        betaKeys1
            .Setup(r => r.TryRedeemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (firstCall) { firstCall = false; return true; }
                return false;
            });
        betaKeys2
            .Setup(r => r.TryRedeemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        players1.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        players1.Setup(r => r.UsernameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        players1.Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Player p, CancellationToken _) => p);
        tokens1.Setup(r => r.CountActiveSessionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        tokens1.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var r1 = await svc1.RegisterAsync(
            new RegisterRequest { Username = "p1", Email = "p1@rota.test", Password = "Secure1Pass", BetaKey = "ROTA-SAME-SAME-SAME" },
            "127.0.0.1");
        var r2 = await svc2.RegisterAsync(
            new RegisterRequest { Username = "p2", Email = "p2@rota.test", Password = "Secure1Pass", BetaKey = "ROTA-SAME-SAME-SAME" },
            "127.0.0.1");

        var successes = new[] { r1, r2 }.Count(x => x is not null);
        successes.Should().Be(1, "exactly one registration should succeed when two race for the same key");
    }

    [Fact]
    public async Task RegisterAsync_BetaGateDisabled_IgnoresBetaKey_Succeeds()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, tokens, betaKeys, _) = BuildAuthService(betaGateEnabled: false, privateKey: key);

        players.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        players.Setup(r => r.UsernameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        players.Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player p, CancellationToken _) => p);

        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "freeplayer", Email = "free@rota.test", Password = "Secure1Pass", BetaKey = "" },
            "127.0.0.1");

        result.Should().NotBeNull("gate disabled → no beta key needed");
        betaKeys.Verify(r => r.TryRedeemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "TryRedeemAsync must NOT be called when the gate is disabled");
    }
}
