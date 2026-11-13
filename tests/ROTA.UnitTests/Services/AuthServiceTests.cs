using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;
using ROTA.UnitTests.TestSupport;

namespace ROTA.UnitTests.Services;

/// <summary>
/// Unit tests for AuthService.
/// Focus: security-critical paths — timing safety, token rotation, session cap, lockout.
/// </summary>
public class AuthServiceTests
{
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
        var checkIn = new Mock<ICheckInService>();
        var resetTokens = new Mock<IPasswordResetTokenRepository>();
        var emails = new Mock<IEmailNotificationService>();

        // Default: not locked out
        lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var service = new AuthService(
            players.Object,
            tokens.Object,
            BuildConfig(privateKey, betaGateEnabled),
            lockout.Object,
            auditLog.Object,
            betaKeys.Object,
            checkIn.Object,
            resetTokens.Object,
            emails.Object, new Mock<IEquipmentService>().Object);

        return (service, players, tokens, lockout, auditLog);
    }

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

    // TOKEN SIGNING — repeated issuance must not dispose the signing key.
    // Regression: a prior `using var rsa = RSA.Create()` disposed the RSA after the first
    // token, and the cached signature provider then threw ObjectDisposedException on later
    // tokens (intermittent 500 on login/register/refresh). Two consecutive logins must both
    // succeed and produce tokens that verify against the matching public key.
    [Fact]
    public async Task LoginAsync_IssuesValidTokens_OnRepeatedCalls()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, tokens, _, _) = BuildService(key);

        var player = MakePlayer();
        players.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        tokens.Setup(r => r.CountActiveSessionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var req = new LoginRequest { Email = "test@rota.test", Password = "Correct1" };

        var first  = await service.LoginAsync(req, "127.0.0.1");
        var second = await service.LoginAsync(req, "127.0.0.1");

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        // Both tokens must verify against the public half of the signing key.
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key);
        using var pub = RSA.Create(rsa.ExportParameters(includePrivateParameters: false));

        var validationParams = new TokenValidationParameters
        {
            ValidIssuer      = "rota-test",
            ValidAudience    = "rota-client",
            IssuerSigningKey = new RsaSecurityKey(pub),
            ValidAlgorithms  = new[] { SecurityAlgorithms.RsaSha256 },
            ValidateLifetime = false,
            ClockSkew        = TimeSpan.Zero,
        };
        var handler = new JwtSecurityTokenHandler();

        handler.Invoking(h => h.ValidateToken(first!.AccessToken, validationParams, out _))
               .Should().NotThrow();
        handler.Invoking(h => h.ValidateToken(second!.AccessToken, validationParams, out _))
               .Should().NotThrow();
    }

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

    // REGISTER — beta gate: a failed registration must NOT burn the single-use key.
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
            lockout.Object, auditLog.Object, betaKeys.Object, new Mock<ICheckInService>().Object,
            new Mock<IPasswordResetTokenRepository>().Object, new Mock<IEmailNotificationService>().Object, new Mock<IEquipmentService>().Object);

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

    // ── Day tracking (TICKET 46, moved to ICheckInService by System 29 S1) ─────────────────────
    // These run the REAL CheckInService over mocked repositories, so they cover the whole path from
    // the auth entry point to the DaysPlayed counter and its achievement metric.

    private static AuthService NewAuthService(
        Mock<IPlayerRepository> players, Mock<IRefreshTokenRepository> tokens, string key, ICheckInService checkIn)
    {
        var lockout = new Mock<IAuthLockoutService>();
        lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return new AuthService(players.Object, tokens.Object, BuildConfig(key), lockout.Object,
            new Mock<IAuditLogRepository>().Object, new Mock<IBetaKeyRepository>().Object, checkIn,
            new Mock<IPasswordResetTokenRepository>().Object, new Mock<IEmailNotificationService>().Object,
            new Mock<IEquipmentService>().Object);
    }

    private static CheckInService RealCheckIn(Mock<IPlayerRepository> players, Mock<IAchievementService> achievements)
    {
        players.SetupMutatePassThrough<CheckInResult>();
        return new CheckInService(players.Object, achievements.Object);
    }

    private static void SetupLogin(Mock<IPlayerRepository> players, Mock<IRefreshTokenRepository> tokens, Player player)
    {
        players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        tokens.Setup(r => r.CountActiveSessionsAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);
    }

    private static void SetupValidRefresh(Mock<IPlayerRepository> players, Mock<IRefreshTokenRepository> tokens, Player player)
    {
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeActiveToken(player.Id));
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        tokens.Setup(r => r.TryMarkRotatedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        tokens.Setup(r => r.CountActiveSessionsAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);
    }

    private static readonly RefreshRequest AnyRefresh = new() { RefreshToken = "somerawtoken" };

    // TICKET 46 — a successful login records the DaysPlayed achievement metric exactly once per UTC
    // day (RecordLogin advances DaysPlayed only on a new calendar day), best-effort and non-blocking.
    [Fact]
    public async Task LoginAsync_Success_RecordsDaysPlayedOncePerDay()
    {
        var key = GenerateTestRsaPrivateKey();
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var achievements = new Mock<IAchievementService>();
        var player = MakePlayer();
        SetupLogin(players, tokens, player);

        var service = NewAuthService(players, tokens, key, RealCheckIn(players, achievements));

        // First login on a fresh player → counts the day.
        await service.LoginAsync(new LoginRequest { Email = player.Email, Password = "Correct1" }, "127.0.0.1");
        player.DaysPlayed.Should().Be(1);
        achievements.Verify(a => a.RecordProgressAsync(
            player.Id, AchievementMetric.DaysPlayed, 1, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Second login same day → no additional day counted, no second record.
        await service.LoginAsync(new LoginRequest { Email = player.Email, Password = "Correct1" }, "127.0.0.1");
        player.DaysPlayed.Should().Be(1);
        achievements.Verify(a => a.RecordProgressAsync(
            player.Id, AchievementMetric.DaysPlayed, 1, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_AchievementHookThrows_DoesNotBreakLogin()
    {
        var key = GenerateTestRsaPrivateKey();
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var achievements = new Mock<IAchievementService>();
        var player = MakePlayer();
        SetupLogin(players, tokens, player);
        // The achievement hook blows up — login must still succeed (best-effort).
        achievements.Setup(a => a.RecordProgressAsync(
            It.IsAny<Guid>(), It.IsAny<AchievementMetric>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = NewAuthService(players, tokens, key, RealCheckIn(players, achievements));

        var result = await service.LoginAsync(new LoginRequest { Email = player.Email, Password = "Correct1" }, "127.0.0.1");

        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
    }

    // System 29 S1 — THE regression. The client resumes on a persisted refresh token, so a returning
    // player may refresh for days without ever calling LoginAsync. Before the fix, none of those days
    // were counted. A refresh on a new UTC day must count it, once.
    [Fact]
    public async Task RefreshAsync_OnANewDay_CountsTheDayOnce()
    {
        var key = GenerateTestRsaPrivateKey();
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var achievements = new Mock<IAchievementService>();
        var player = MakePlayer();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        player.RecordLogin(today.AddDays(-3));   // last password login three days ago
        SetupValidRefresh(players, tokens, player);

        var service = NewAuthService(players, tokens, key, RealCheckIn(players, achievements));

        (await service.RefreshAsync(AnyRefresh, "127.0.0.1")).Should().NotBeNull();
        player.DaysPlayed.Should().Be(2);
        player.LastLoginDate.Should().Be(today);
        achievements.Verify(a => a.RecordProgressAsync(
            player.Id, AchievementMetric.DaysPlayed, 1, $"ach:day:{player.Id}:{today:yyyy-MM-dd}",
            It.IsAny<CancellationToken>()), Times.Once);

        // Refreshing again later the same day (every ≤15 min of play) must not count again.
        (await service.RefreshAsync(AnyRefresh, "127.0.0.1")).Should().NotBeNull();
        player.DaysPlayed.Should().Be(2);
        achievements.Verify(a => a.RecordProgressAsync(
            player.Id, AchievementMetric.DaysPlayed, It.IsAny<long>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_CheckInThrows_StillIssuesTokens()
    {
        var key = GenerateTestRsaPrivateKey();
        var players = new Mock<IPlayerRepository>();
        var tokens  = new Mock<IRefreshTokenRepository>();
        var checkIn = new Mock<ICheckInService>();
        checkIn.Setup(c => c.CheckInAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("db down"));
        var player = MakePlayer();
        SetupValidRefresh(players, tokens, player);

        var service = NewAuthService(players, tokens, key, checkIn.Object);

        var result = await service.RefreshAsync(AnyRefresh, "127.0.0.1");

        result.Should().NotBeNull("day tracking is best-effort and must never break a refresh");
        result!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshAsync_LostRotationRace_DoesNotCheckIn()
    {
        var key = GenerateTestRsaPrivateKey();
        var players = new Mock<IPlayerRepository>();
        var tokens  = new Mock<IRefreshTokenRepository>();
        var checkIn = new Mock<ICheckInService>();
        var player = MakePlayer();
        SetupValidRefresh(players, tokens, player);
        tokens.Setup(r => r.TryMarkRotatedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var service = NewAuthService(players, tokens, key, checkIn.Object);

        (await service.RefreshAsync(AnyRefresh, "127.0.0.1")).Should().BeNull();
        checkIn.Verify(c => c.CheckInAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "only a refresh that actually rotated counts as showing up");
    }

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
        // Audit fix: rotation is now an atomic conditional UPDATE — this caller wins the latch.
        tokens.Setup(r => r.TryMarkRotatedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        tokens.Setup(r => r.CountActiveSessionsAsync(player.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var result = await service.RefreshAsync(
            new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        result.Should().NotBeNull();
        tokens.Verify(r => r.TryMarkRotatedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "consumed token must be revoked (atomically) on rotation");
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<RefreshTokenRevokeReason>(), It.IsAny<CancellationToken>()), Times.Never,
            "a clean rotation must not trigger the breach response");
    }

    [Fact]
    public async Task RefreshAsync_LostRotationRace_RevokesAllSessions()
    {
        // Audit fix — two concurrent refreshes with the same token: the loser of the conditional
        // UPDATE must NOT mint a second token pair, and the family is revoked (replay semantics).
        var (service, players, tokens, _, _) = BuildService();
        var player = MakePlayer();
        var activeToken = MakeActiveToken(player.Id);

        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(activeToken);
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        tokens.Setup(r => r.TryMarkRotatedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);   // a concurrent request already rotated it
        tokens.Setup(r => r.FindRevokeReasonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(RefreshTokenRevokeReason.Rotated);

        var result = await service.RefreshAsync(
            new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        result.Should().BeNull("the latch loser must not mint a second session");
        tokens.Verify(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(r => r.RevokeAllActiveAsync(player.Id, RefreshTokenRevokeReason.ReplayDetected, It.IsAny<CancellationToken>()), Times.Once,
            "concurrent reuse of one refresh token is treated as theft");
    }

    [Fact]
    public async Task RefreshAsync_LostRotationRaceToALogout_FailsQuietly()
    {
        // The token was live when read, but a logout on the same session committed before our
        // conditional UPDATE. That is a deliberate end of session, not a second copy of the token.
        var (service, players, tokens, _, auditLog) = BuildService();
        var player = MakePlayer();
        var activeToken = MakeActiveToken(player.Id);

        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(activeToken);
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        tokens.Setup(r => r.TryMarkRotatedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);
        tokens.Setup(r => r.FindRevokeReasonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(RefreshTokenRevokeReason.LoggedOut);

        var result = await service.RefreshAsync(
            new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        result.Should().BeNull();
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<RefreshTokenRevokeReason>(), It.IsAny<CancellationToken>()), Times.Never,
            "a logout racing a refresh must not log the player out of every other device");
        auditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(l => l.Action == "TokenReplayDetected"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_RotatedToken_RevokesEveryActiveSessionAsReplay()
    {
        var (service, _, tokens, _, auditLog) = BuildService();
        var revokedToken = new RefreshToken(Guid.NewGuid(), "anyhash", DateTimeOffset.UtcNow.AddDays(7), null);
        revokedToken.Revoke(RefreshTokenRevokeReason.Rotated);
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(revokedToken);

        var result = await service.RefreshAsync(
            new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        result.Should().BeNull("revoked tokens must be rejected");
        tokens.Verify(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        // Replaying a rotated token is the theft signal — the whole family is revoked.
        tokens.Verify(r => r.RevokeAllActiveAsync(revokedToken.PlayerId, RefreshTokenRevokeReason.ReplayDetected, It.IsAny<CancellationToken>()), Times.Once,
            "replay of a rotated token must evict every session (breach detection)");
        auditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(l => l.Action == "TokenReplayDetected"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // The bug this pins: every one of these used to be treated as a replayed rotation, so a stale tab
    // after logout, or the oldest device after a fourth login, signed the player out everywhere and was
    // filed as a breach.
    [Theory]
    [InlineData(RefreshTokenRevokeReason.LoggedOut)]
    [InlineData(RefreshTokenRevokeReason.SessionLimit)]
    [InlineData(RefreshTokenRevokeReason.PasswordReset)]
    [InlineData(RefreshTokenRevokeReason.ReplayDetected)]
    [InlineData(RefreshTokenRevokeReason.RoleRevoked)]
    [InlineData(RefreshTokenRevokeReason.Banned)]
    public async Task RefreshAsync_TokenEndedOnPurpose_FailsWithoutTouchingOtherSessions(RefreshTokenRevokeReason reason)
    {
        var (service, _, tokens, _, auditLog) = BuildService();
        var revokedToken = new RefreshToken(Guid.NewGuid(), "anyhash", DateTimeOffset.UtcNow.AddDays(7), null);
        revokedToken.Revoke(reason);
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(revokedToken);

        var result = await service.RefreshAsync(
            new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        result.Should().BeNull("a revoked token never refreshes, whatever the reason");
        tokens.Verify(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<RefreshTokenRevokeReason>(), It.IsAny<CancellationToken>()), Times.Never,
            $"a session ended on purpose ({reason}) is not a theft signal");
        auditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(l => l.Action == "TokenReplayDetected"), It.IsAny<CancellationToken>()), Times.Never);
        auditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(l => l.Action == "TokenRefreshFailed"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_RecordsLoggedOut()
    {
        var (service, _, tokens, _, _) = BuildService();
        var token = MakeActiveToken(Guid.NewGuid());
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(token);

        await service.LogoutAsync(new RefreshRequest { RefreshToken = "somerawtoken" });

        tokens.Verify(r => r.RevokeAsync(token, RefreshTokenRevokeReason.LoggedOut, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_AtSessionCap_PushesOutOldestAsSessionLimit()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, tokens, _, _) = BuildService(key);
        var player = MakePlayer();
        var oldest = MakeActiveToken(player.Id);

        players.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        tokens.Setup(r => r.CountActiveSessionsAsync(player.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(3);
        tokens.Setup(r => r.FindOldestActiveAsync(player.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(oldest);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var result = await service.LoginAsync(
            new LoginRequest { Email = "test@rota.test", Password = "Correct1" }, "127.0.0.1");

        result.Should().NotBeNull();
        tokens.Verify(r => r.RevokeAsync(oldest, RefreshTokenRevokeReason.SessionLimit, It.IsAny<CancellationToken>()), Times.Once,
            "the pushed-out device must later fail quietly, not read as a replay");
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
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<RefreshTokenRevokeReason>(), It.IsAny<CancellationToken>()), Times.Never,
            "an expired (not replayed) token is an ordinary failure, not a breach");
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_IsIdempotent_NoException()
    {
        var (service, _, tokens, _, _) = BuildService();
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken?)null);

        var act = async () => await service.LogoutAsync(new RefreshRequest { RefreshToken = "unknown" });

        await act.Should().NotThrowAsync("double-logout or unknown token must be silently ignored");
        tokens.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<RefreshTokenRevokeReason>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── S8: change password while logged in, log out everywhere (2026-11-12) ──

    [Fact]
    public async Task ChangePassword_CorrectCurrent_SetsNewHash_RevokesAllSessions_ThenIssuesOneNewPair()
    {
        var key = GenerateTestRsaPrivateKey();
        var (service, players, tokens, _, auditLog) = BuildService(key);
        var player = MakePlayer();
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        var order = new List<string>();
        tokens.Setup(r => r.RevokeAllActiveAsync(player.Id, RefreshTokenRevokeReason.PasswordChanged, It.IsAny<CancellationToken>()))
              .Callback(() => order.Add("revoke-all")).Returns(Task.CompletedTask);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .Callback(() => order.Add("create"));

        var result = await service.ChangePasswordAsync(player.Id,
            new ChangePasswordRequest { CurrentPassword = "Correct1", NewPassword = "Brandnew2" }, "127.0.0.1");

        result.Status.Should().Be(ChangePasswordStatus.Success);
        result.Tokens.Should().NotBeNull();
        result.Tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        BCrypt.Net.BCrypt.Verify("Brandnew2", player.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("Correct1", player.PasswordHash).Should().BeFalse();
        order.Should().Equal(new[] { "revoke-all", "create" },
            "the new session must be created after the sweep, or the sweep would end it too");
        auditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(l => l.Action == "PasswordChanged"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_ChangesNothing_AndIsAudited()
    {
        var (service, players, tokens, _, auditLog) = BuildService();
        var player = MakePlayer();
        var hashBefore = player.PasswordHash;
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        var result = await service.ChangePasswordAsync(player.Id,
            new ChangePasswordRequest { CurrentPassword = "Wrong1xx", NewPassword = "Brandnew2" }, "127.0.0.1");

        result.Status.Should().Be(ChangePasswordStatus.WrongPassword);
        result.Tokens.Should().BeNull();
        player.PasswordHash.Should().Be(hashBefore);
        players.Verify(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<RefreshTokenRevokeReason>(), It.IsAny<CancellationToken>()), Times.Never);
        auditLog.Verify(a => a.AppendAsync(It.Is<AuditLog>(l => l.Action == "PasswordChangeFailed"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_SameAsCurrent_OrBannedAccount_IsRefused()
    {
        var (service, players, tokens, _, _) = BuildService();
        var player = MakePlayer();
        var banned = MakePlayer(isBanned: true);
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        players.Setup(r => r.FindByIdAsync(banned.Id, It.IsAny<CancellationToken>())).ReturnsAsync(banned);

        (await service.ChangePasswordAsync(player.Id,
            new ChangePasswordRequest { CurrentPassword = "Correct1", NewPassword = "Correct1" }, "ip"))
            .Status.Should().Be(ChangePasswordStatus.SamePassword);
        (await service.ChangePasswordAsync(banned.Id,
            new ChangePasswordRequest { CurrentPassword = "Correct1", NewPassword = "Brandnew2" }, "ip"))
            .Status.Should().Be(ChangePasswordStatus.NotFound);
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<RefreshTokenRevokeReason>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutEverywhere_RevokesEverySession_WithItsOwnReason()
    {
        var (service, players, tokens, _, _) = BuildService();
        var player = MakePlayer();
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        (await service.LogoutEverywhereAsync(player.Id, "ip")).Should().BeTrue();
        (await service.LogoutEverywhereAsync(Guid.NewGuid(), "ip")).Should().BeFalse();

        tokens.Verify(r => r.RevokeAllActiveAsync(player.Id, RefreshTokenRevokeReason.LoggedOutEverywhere, It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<RefreshTokenRevokeReason>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Correct1", "short1A", false)]          // under 8
    [InlineData("Correct1", "alllowercase1", false)]    // no uppercase
    [InlineData("Correct1", "NoDigitsHere", false)]     // no digit
    [InlineData("", "Brandnew2", false)]                // current password missing
    [InlineData("Correct1", "Brandnew2", true)]
    public void ChangePasswordValidator_HoldsTheRegistrationBar(string current, string next, bool valid)
    {
        var v = new ROTA.Application.Validators.ChangePasswordRequestValidator();
        v.Validate(new ChangePasswordRequest { CurrentPassword = current, NewPassword = next })
         .IsValid.Should().Be(valid);
    }
}
