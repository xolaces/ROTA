using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Seeding;

namespace ROTA.IntegrationTests;

/// <summary>
/// Unit-style tests for SeedData.EnsureAdminAsync using a real ServiceCollection
/// but mocked repositories. No DB or containers required.
/// </summary>
public class SeedDataTests
{
    // -----------------------------------------------------------------------
    // FIXTURES
    // -----------------------------------------------------------------------

    private static IServiceProvider BuildProvider(
        IConfiguration config,
        IPlayerRepository players,
        IAuditLogRepository audit)
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(players);
        services.AddSingleton(audit);
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(string? adminPassword, string? adminEmail = null)
    {
        var dict = new Dictionary<string, string?>();
        if (adminPassword is not null)
            dict["Seed:AdminPassword"] = adminPassword;
        if (adminEmail is not null)
            dict["Seed:AdminEmail"] = adminEmail;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // -----------------------------------------------------------------------
    // TESTS
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EnsureAdminAsync_FirstCall_CreatesAdminPlayer()
    {
        var playersMock = new Mock<IPlayerRepository>();
        var auditMock   = new Mock<IAuditLogRepository>();

        // No existing admin.
        playersMock
            .Setup(r => r.UsernameExistsAsync("Xolaces", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        playersMock
            .Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player p, CancellationToken _) => p);

        var sp = BuildProvider(BuildConfig("SuperSecret1!"), playersMock.Object, auditMock.Object);

        await SeedData.EnsureAdminAsync(sp);

        playersMock.Verify(r => r.CreateAsync(
            It.Is<Player>(p =>
                p.Username == "Xolaces" &&
                p.HasRole(PlayerRoles.Admin) &&
                p.HasRole(PlayerRoles.Player) &&
                p.DisplayName == "DEV_Xolaces"),
            It.IsAny<CancellationToken>()), Times.Once,
            "admin must be created with correct username, roles, and display name");

        auditMock.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "AdminSeeded"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureAdminAsync_SecondCall_IsNoOp()
    {
        var playersMock = new Mock<IPlayerRepository>();
        var auditMock   = new Mock<IAuditLogRepository>();

        // Password is configured AND admin already exists — pure no-op.
        playersMock
            .Setup(r => r.UsernameExistsAsync("Xolaces", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sp = BuildProvider(BuildConfig("SuperSecret1!"), playersMock.Object, auditMock.Object);

        await SeedData.EnsureAdminAsync(sp);

        playersMock.Verify(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()),
            Times.Never, "CreateAsync must not be called when the admin already exists");
        auditMock.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureAdminAsync_MissingPassword_DoesNotCreatePlayerAndDoesNotThrow()
    {
        var playersMock = new Mock<IPlayerRepository>();
        var auditMock   = new Mock<IAuditLogRepository>();

        playersMock
            .Setup(r => r.UsernameExistsAsync("Xolaces", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // No Seed:AdminPassword in config.
        var sp = BuildProvider(BuildConfig(adminPassword: null), playersMock.Object, auditMock.Object);

        var act = async () => await SeedData.EnsureAdminAsync(sp);

        await act.Should().NotThrowAsync("missing password must log a warning and return gracefully");
        playersMock.Verify(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()),
            Times.Never, "CreateAsync must not be called when Seed:AdminPassword is missing");
    }

    [Fact]
    public async Task EnsureAdminAsync_CustomEmail_UsedInPlayerCreation()
    {
        var playersMock = new Mock<IPlayerRepository>();
        var auditMock   = new Mock<IAuditLogRepository>();

        playersMock
            .Setup(r => r.UsernameExistsAsync("Xolaces", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        playersMock
            .Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player p, CancellationToken _) => p);

        var sp = BuildProvider(
            BuildConfig("SuperSecret1!", adminEmail: "custom@rota.dev"),
            playersMock.Object, auditMock.Object);

        await SeedData.EnsureAdminAsync(sp);

        playersMock.Verify(r => r.CreateAsync(
            It.Is<Player>(p => p.Email == "custom@rota.dev"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
