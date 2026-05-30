using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

/// <summary>
/// Unit tests for AdminService: role grant/revoke guards, audit, and session revocation.
/// </summary>
public class AdminServiceTests
{
    // -----------------------------------------------------------------------
    // FIXTURES
    // -----------------------------------------------------------------------

    private static (AdminService service,
                    Mock<IPlayerRepository> players,
                    Mock<IRefreshTokenRepository> tokens,
                    Mock<IAuditLogRepository> auditLog)
        BuildService()
    {
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var auditLog = new Mock<IAuditLogRepository>();
        var service  = new AdminService(players.Object, tokens.Object, auditLog.Object);
        return (service, players, tokens, auditLog);
    }

    private static Player MakeAdmin(string username = "admin") =>
        MakePlayer(username, PlayerRoles.Player | PlayerRoles.Admin);

    private static Player MakePlayer(string username = "player", PlayerRoles roles = PlayerRoles.Player)
    {
        var p = Player.Create(username, $"{username}@rota.test", "hash");
        if (roles.HasFlag(PlayerRoles.Admin))    p.GrantRole(PlayerRoles.Admin);
        if (roles.HasFlag(PlayerRoles.Moderator)) p.GrantRole(PlayerRoles.Moderator);
        return p;
    }

    // -----------------------------------------------------------------------
    // GRANT ROLE — success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GrantRoleAsync_ValidAdmin_GrantsModeratorRole()
    {
        var (service, players, _, auditLog) = BuildService();
        var actor  = MakeAdmin();
        var target = MakePlayer("target");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.GrantRoleAsync(actor.Id, "target", PlayerRoles.Moderator);

        result.Success.Should().BeTrue();
        target.HasRole(PlayerRoles.Moderator).Should().BeTrue("Moderator flag must be set after grant");
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "RoleGranted"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantRoleAsync_ByGuid_ResolvesTarget()
    {
        var (service, players, _, _) = BuildService();
        var actor  = MakeAdmin();
        var target = MakePlayer("target");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.GrantRoleAsync(actor.Id, target.Id.ToString(), PlayerRoles.Moderator);

        result.Success.Should().BeTrue();
        players.Verify(r => r.FindByIdAsync(target.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "target should be resolved by GUID when the string is a valid GUID");
    }

    [Fact]
    public async Task GrantRoleAsync_CliActor_SkipsDbActorCheck()
    {
        var (service, players, _, _) = BuildService();
        var target = MakePlayer("target");

        // Guid.Empty is the CLI actor — no actor DB lookup should happen.
        players.Setup(r => r.FindByUsernameAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.GrantRoleAsync(Guid.Empty, "target", PlayerRoles.Moderator);

        result.Success.Should().BeTrue();
        players.Verify(r => r.FindByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()), Times.Never,
            "actor DB lookup must be skipped for Guid.Empty (CLI)");
    }

    // -----------------------------------------------------------------------
    // GRANT ROLE — guards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GrantRoleAsync_NonAdminActor_ReturnsFail()
    {
        var (service, players, _, _) = BuildService();
        var nonAdmin = MakePlayer("regular");

        players.Setup(r => r.FindByIdAsync(nonAdmin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(nonAdmin);

        var result = await service.GrantRoleAsync(nonAdmin.Id, "target", PlayerRoles.Moderator);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not an admin");
    }

    [Fact]
    public async Task GrantRoleAsync_TargetNotFound_ReturnsFail()
    {
        var (service, players, _, _) = BuildService();
        var actor = MakeAdmin();

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var result = await service.GrantRoleAsync(actor.Id, "ghost", PlayerRoles.Moderator);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
    }

    [Fact]
    public async Task GrantRoleAsync_PlayerRoleFlag_ReturnsFail()
    {
        var (service, players, _, _) = BuildService();
        var actor = MakeAdmin();

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);

        var result = await service.GrantRoleAsync(actor.Id, "target", PlayerRoles.Player);

        result.Success.Should().BeFalse("cannot grant the base Player role");
    }

    // -----------------------------------------------------------------------
    // REVOKE ROLE — success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RevokeRoleAsync_Admin_RemovesModeratorAndRevokesTokens()
    {
        var (service, players, tokens, auditLog) = BuildService();
        var actor  = MakeAdmin();
        var target = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("mod", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tokens.Setup(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.RevokeRoleAsync(actor.Id, "mod", PlayerRoles.Moderator);

        result.Success.Should().BeTrue();
        target.HasRole(PlayerRoles.Moderator).Should().BeFalse("Moderator flag must be removed");
        target.HasRole(PlayerRoles.Player).Should().BeTrue("Player flag must never be removed");
        tokens.Verify(r => r.RevokeAllActiveAsync(target.Id, It.IsAny<CancellationToken>()), Times.Once,
            "revoke sessions when a privilege role is removed");
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "RoleRevoked"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // REVOKE ROLE — last-admin guard
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RevokeRoleAsync_LastAdmin_ReturnsFail()
    {
        var (service, players, _, _) = BuildService();
        var actor  = MakeAdmin("actor-admin");
        var target = MakeAdmin("last-admin");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("last-admin", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        // Only 1 admin in the system.
        players.Setup(r => r.CountByRoleAsync(PlayerRoles.Admin, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await service.RevokeRoleAsync(actor.Id, "last-admin", PlayerRoles.Admin);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("last admin");
    }

    [Fact]
    public async Task RevokeRoleAsync_NotLastAdmin_Succeeds()
    {
        var (service, players, tokens, _) = BuildService();
        var actor  = MakeAdmin("actor");
        var target = MakeAdmin("second-admin");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("second-admin", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        // 2 admins — safe to remove one.
        players.Setup(r => r.CountByRoleAsync(PlayerRoles.Admin, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tokens.Setup(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.RevokeRoleAsync(actor.Id, "second-admin", PlayerRoles.Admin);

        result.Success.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // REVOKE ROLE — guards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RevokeRoleAsync_PlayerRole_ReturnsFail()
    {
        var (service, players, _, _) = BuildService();
        var actor = MakeAdmin();

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);

        var result = await service.RevokeRoleAsync(actor.Id, "target", PlayerRoles.Player);

        result.Success.Should().BeFalse("cannot revoke the base Player role");
    }

    [Fact]
    public async Task RevokeRoleAsync_NonAdminActor_ReturnsFail()
    {
        var (service, players, _, _) = BuildService();
        var nonAdmin = MakePlayer("regular");

        players.Setup(r => r.FindByIdAsync(nonAdmin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(nonAdmin);

        var result = await service.RevokeRoleAsync(nonAdmin.Id, "target", PlayerRoles.Moderator);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not an admin");
    }
}
