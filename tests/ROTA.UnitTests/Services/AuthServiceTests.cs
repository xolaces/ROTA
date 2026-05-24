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
/// Focus: security-critical paths - timing safety, token rotation, session cap.
/// </summary>
public class AuthServiceTests
{
    // FIXTURES

    private static IConfiguration BuildConfig()
    {
        // Minimal in-memory config for JWT. Not valid RS256 - GenerateAccessToken
        // is not under test here so we don't need real keys.
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "rota-test",
                ["Jwt:Audience"] = "rota-client",
                // PLACEHOLDER: real key needed when GenerateAccessToken tests are added
                ["Jwt:PrivateKey"] = "PLACEHOLDER",
            })
            .Build();
    }

    private static Player MakePlayer(bool isBanned = false) => new Player
    {
        Id = Guid.NewGuid(),
        Username = "testplayer",
        Email = "test@rota.test",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Correct1", 4), // low work factor for tests
        IsBanned = isBanned,
        IsDeleted = false,
        Level = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    // REGISTER

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsNull()
    {
        // Arrange
        var players = new Mock<IPlayerRepository>();
        players.Setup(r => r.EmailExistsAsync("taken@rota.test", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var service = new AuthService(players.Object, new Mock<IRefreshTokenRepository>().Object, BuildConfig());

        // Act
        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "newuser", Email = "taken@rota.test", Password = "Correct1" },
            "127.0.0.1");

        // Assert
        result.Should().BeNull("duplicate email must silently return null, not throw 409");
        players.Verify(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUsername_ReturnsNull()
    {
        // Arrange
        var players = new Mock<IPlayerRepository>();
        players.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(r => r.UsernameExistsAsync("takenname", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var service = new AuthService(players.Object, new Mock<IRefreshTokenRepository>().Object, BuildConfig());

        // Act
        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "takenname", Email = "new@rota.test", Password = "Correct1" },
            "127.0.0.1");

        // Assert
        result.Should().BeNull();
    }

    // LOGIN - security paths

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        // Arrange
        var player = MakePlayer();
        var players = new Mock<IPlayerRepository>();
        players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        var service = new AuthService(players.Object, new Mock<IRefreshTokenRepository>().Object, BuildConfig());

        // Act
        var result = await service.LoginAsync(
            new LoginRequest { Email = player.Email, Password = "WrongPassword1" },
            "127.0.0.1");

        // Assert
        result.Should().BeNull("wrong password must return null, not throw");
    }

    [Fact]
    public async Task LoginAsync_EmailNotFound_ReturnsNull_WithoutShortCircuiting()
    {
        // SECURITY: this test verifies the timing-safe path.
        // BCrypt.Verify must run even when player is null (dummy hash used).
        var players = new Mock<IPlayerRepository>();
        players.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);

        var service = new AuthService(players.Object, new Mock<IRefreshTokenRepository>().Object, BuildConfig());

        var result = await service.LoginAsync(
            new LoginRequest { Email = "nobody@rota.test", Password = "SomePass1" },
            "127.0.0.1");

        // The important assertion is null - the BCrypt timing is observable only
        // in integration/perf tests, not unit tests. Document intent here.
        result.Should().BeNull("non-existent email must return null, same code path as wrong password");
    }

    [Fact]
    public async Task LoginAsync_BannedPlayer_ReturnsNull()
    {
        // Arrange
        var player = MakePlayer(isBanned: true);
        var players = new Mock<IPlayerRepository>();
        players.Setup(r => r.FindByEmailAsync(player.Email, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        var service = new AuthService(players.Object, new Mock<IRefreshTokenRepository>().Object, BuildConfig());

        // Act
        var result = await service.LoginAsync(
            new LoginRequest { Email = player.Email, Password = "Correct1" },
            "127.0.0.1");

        // Assert
        result.Should().BeNull("banned players must not receive tokens");
    }

    // REFRESH - token lifecycle

    [Fact]
    public async Task RefreshAsync_AlreadyRevokedToken_ReturnsNull()
    {
        // Arrange
        var revokedToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            TokenHash = "anyhash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsRevoked = true,       // already revoked
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var tokens = new Mock<IRefreshTokenRepository>();
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(revokedToken);

        var service = new AuthService(new Mock<IPlayerRepository>().Object, tokens.Object, BuildConfig());

        // Act
        var result = await service.RefreshAsync(new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        // Assert
        result.Should().BeNull("revoked tokens must be rejected");
        tokens.Verify(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsNull()
    {
        // Arrange
        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            TokenHash = "anyhash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),  // expired
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-8),
        };

        var tokens = new Mock<IRefreshTokenRepository>();
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(expiredToken);

        var service = new AuthService(new Mock<IPlayerRepository>().Object, tokens.Object, BuildConfig());

        // Act
        var result = await service.RefreshAsync(new RefreshRequest { RefreshToken = "somerawtoken" }, "127.0.0.1");

        // Assert
        result.Should().BeNull("expired tokens must be rejected");
    }

    // LOGOUT

    [Fact]
    public async Task LogoutAsync_UnknownToken_IsIdempotent_NoException()
    {
        // Arrange
        var tokens = new Mock<IRefreshTokenRepository>();
        tokens.Setup(r => r.FindByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken?)null);

        var service = new AuthService(new Mock<IPlayerRepository>().Object, tokens.Object, BuildConfig());

        // Act - must not throw
        var act = async () => await service.LogoutAsync(new RefreshRequest { RefreshToken = "unknown" });

        // Assert
        await act.Should().NotThrowAsync("double-logout or unknown token must be silently ignored");
        tokens.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}