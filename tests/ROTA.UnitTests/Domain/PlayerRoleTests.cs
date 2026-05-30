using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Domain;

/// <summary>
/// Unit tests for the Component A role system:
/// GrantRole / RevokeRole / HasRole on Player, and JWT role claims from AuthService.
/// </summary>
public class PlayerRoleTests
{
    // -----------------------------------------------------------------------
    // GrantRole / RevokeRole / HasRole
    // -----------------------------------------------------------------------

    [Fact]
    public void Player_DefaultRoles_IsPlayer()
    {
        var player = Player.Create("alice", "alice@rota.test", "hash");
        player.Roles.Should().Be(PlayerRoles.Player);
        player.HasRole(PlayerRoles.Player).Should().BeTrue();
        player.HasRole(PlayerRoles.Admin).Should().BeFalse();
        player.HasRole(PlayerRoles.Moderator).Should().BeFalse();
    }

    [Fact]
    public void GrantRole_Admin_AddsAdminFlag()
    {
        var player = Player.Create("bob", "bob@rota.test", "hash");
        player.GrantRole(PlayerRoles.Admin);

        player.HasRole(PlayerRoles.Admin).Should().BeTrue();
        player.HasRole(PlayerRoles.Player).Should().BeTrue("Player flag must still be set");
    }

    [Fact]
    public void GrantRole_Moderator_AddsModeratorFlag()
    {
        var player = Player.Create("carol", "carol@rota.test", "hash");
        player.GrantRole(PlayerRoles.Moderator);

        player.HasRole(PlayerRoles.Moderator).Should().BeTrue();
        player.HasRole(PlayerRoles.Player).Should().BeTrue();
    }

    [Fact]
    public void RevokeRole_Admin_RemovesAdminFlag_KeepsPlayer()
    {
        var player = Player.Create("dave", "dave@rota.test", "hash");
        player.GrantRole(PlayerRoles.Admin);
        player.RevokeRole(PlayerRoles.Admin);

        player.HasRole(PlayerRoles.Admin).Should().BeFalse("Admin was revoked");
        player.HasRole(PlayerRoles.Player).Should().BeTrue("Player is permanent — never removed");
    }

    [Fact]
    public void RevokeRole_Player_NeverRemovesPlayerFlag()
    {
        var player = Player.Create("eve", "eve@rota.test", "hash");

        // Attempt to revoke the Player flag directly
        player.RevokeRole(PlayerRoles.Player);

        player.HasRole(PlayerRoles.Player).Should().BeTrue(
            "RevokeRole must NEVER strip the base Player flag");
    }

    [Fact]
    public void RevokeRole_NonHeldRole_IsIdempotent()
    {
        var player = Player.Create("frank", "frank@rota.test", "hash");
        // Moderator was never granted
        player.RevokeRole(PlayerRoles.Moderator);

        player.HasRole(PlayerRoles.Player).Should().BeTrue();
        player.HasRole(PlayerRoles.Moderator).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // JWT role claims
    // -----------------------------------------------------------------------

    private static string GenerateTestRsaPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static IConfiguration BuildConfig(string privateKey)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"]       = "rota-test",
                ["Jwt:Audience"]     = "rota-client",
                ["Jwt:PrivateKey"]   = privateKey,
                ["BetaGate:Enabled"] = "false",
            })
            .Build();

    private static AuthService BuildAuthService(string privateKey)
    {
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var lockout  = new Mock<IAuthLockoutService>();
        var auditLog = new Mock<IAuditLogRepository>();
        var betaKeys = new Mock<IBetaKeyRepository>();

        lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

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

        return new AuthService(players.Object, tokens.Object, BuildConfig(privateKey), lockout.Object, auditLog.Object, betaKeys.Object);
    }

    [Fact]
    public async Task JWT_PlayerOnly_ContainsPlayerRoleClaim_NoAdmin()
    {
        var key = GenerateTestRsaPrivateKey();
        var service = BuildAuthService(key);

        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "gamer", Email = "gamer@rota.test", Password = "Secure1Pass" },
            "127.0.0.1");

        result.Should().NotBeNull();
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result!.AccessToken);

        var roles = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().Contain(nameof(PlayerRoles.Player));
        roles.Should().NotContain(nameof(PlayerRoles.Admin));
        roles.Should().NotContain(nameof(PlayerRoles.Moderator));
        roles.Should().NotContain(nameof(PlayerRoles.None));
    }

    [Fact]
    public async Task JWT_AdminPlayer_ContainsBothPlayerAndAdminClaims()
    {
        var key = GenerateTestRsaPrivateKey();

        // Build a player who has Admin role
        var adminPlayer = Player.Create("adminuser", "admin@rota.test",
            BCrypt.Net.BCrypt.HashPassword("Secure1Pass", 4));
        adminPlayer.GrantRole(PlayerRoles.Admin);

        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var lockout  = new Mock<IAuthLockoutService>();
        var auditLog = new Mock<IAuditLogRepository>();

        lockout.Setup(l => l.IsLockedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(r => r.FindByEmailAsync("admin@rota.test", It.IsAny<CancellationToken>()))
               .ReturnsAsync(adminPlayer);
        tokens.Setup(r => r.CountActiveSessionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);
        tokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        var betaKeysMock = new Mock<IBetaKeyRepository>();
        var service = new AuthService(players.Object, tokens.Object, BuildConfig(key), lockout.Object, auditLog.Object, betaKeysMock.Object);

        var result = await service.LoginAsync(
            new LoginRequest { Email = "admin@rota.test", Password = "Secure1Pass" },
            "127.0.0.1");

        result.Should().NotBeNull();
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result!.AccessToken);

        var roles = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().Contain(nameof(PlayerRoles.Player));
        roles.Should().Contain(nameof(PlayerRoles.Admin));
    }

    [Fact]
    public async Task JWT_ContainsDisplayNameClaim()
    {
        var key = GenerateTestRsaPrivateKey();
        var service = BuildAuthService(key);

        var result = await service.RegisterAsync(
            new RegisterRequest { Username = "showme", Email = "show@rota.test", Password = "Secure1Pass" },
            "127.0.0.1");

        result.Should().NotBeNull();
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result!.AccessToken);

        jwt.Claims.FirstOrDefault(c => c.Type == "display_name")?.Value
           .Should().Be("showme", "display_name claim must match the player's DisplayName");
    }
}
