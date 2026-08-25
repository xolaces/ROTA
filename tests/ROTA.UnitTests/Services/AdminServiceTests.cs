using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

/// <summary>
/// Unit tests for AdminService: role grant/revoke guards, audit, and session revocation.
/// </summary>
public class AdminServiceTests
{
    private static (AdminService service,
                    Mock<IPlayerRepository> players,
                    Mock<IRefreshTokenRepository> tokens,
                    Mock<IAuditLogRepository> auditLog)
        BuildService()
    {
        var (service, players, tokens, auditLog, _) = BuildServiceEx();
        return (service, players, tokens, auditLog);
    }

    private static (AdminService service,
                    Mock<IPlayerRepository> players,
                    Mock<IRefreshTokenRepository> tokens,
                    Mock<IAuditLogRepository> auditLog,
                    Mock<IEmailNotificationService> emails)
        BuildServiceEx()
    {
        var players  = new Mock<IPlayerRepository>();
        var tokens   = new Mock<IRefreshTokenRepository>();
        var auditLog = new Mock<IAuditLogRepository>();
        var emails   = new Mock<IEmailNotificationService>();
        var service  = new AdminService(players.Object, tokens.Object, auditLog.Object, emails.Object);
        return (service, players, tokens, auditLog, emails);
    }

    private static Player MakeAdmin(string username = "admin") =>
        MakePlayer(username, PlayerRoles.Player | PlayerRoles.Admin);

    private static Player MakePlayer(string username = "player", PlayerRoles roles = PlayerRoles.Player)
    {
        var p = Player.Create(username, $"{username}@rota.test", "hash");
        if (roles.HasFlag(PlayerRoles.Admin))     p.GrantRole(PlayerRoles.Admin);
        if (roles.HasFlag(PlayerRoles.Moderator)) p.GrantRole(PlayerRoles.Moderator);
        if (roles.HasFlag(PlayerRoles.Developer)) p.GrantRole(PlayerRoles.Developer);
        return p;
    }

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

    [Fact]
    public async Task RevokeRoleAsync_LastAdmin_ReturnsFail()
    {
        var (service, players, _, _) = BuildService();
        var actor  = MakeAdmin("actor-admin");
        var target = MakeAdmin("last-admin");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("last-admin", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        // Audit fix: the last-admin guard is now the atomic demotion. Last admin → returns false.
        players.Setup(r => r.TryDemoteAdminAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

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
        // Audit fix: 2 admins — the atomic demotion succeeds.
        players.Setup(r => r.TryDemoteAdminAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tokens.Setup(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.RevokeRoleAsync(actor.Id, "second-admin", PlayerRoles.Admin);

        result.Success.Should().BeTrue();
        target.HasRole(PlayerRoles.Admin).Should().BeFalse("the demotion clears the Admin flag");
        tokens.Verify(r => r.RevokeAllActiveAsync(target.Id, It.IsAny<CancellationToken>()), Times.Once,
            "demoting an admin must revoke their sessions");
    }

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

    // MODERATION — ban / mute / unmute (T40)

    [Fact]
    public async Task BanPlayerAsync_ValidAdmin_Bans_RevokesSessions_Audits_AndEmails()
    {
        var (service, players, tokens, auditLog, emails) = BuildServiceEx();
        var actor  = MakeAdmin("boss");
        var target = MakePlayer("baddie");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tokens.Setup(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.BanPlayerAsync(actor.Id, "baddie", "cheating");

        result.Success.Should().BeTrue();
        target.IsBanned.Should().BeTrue();
        tokens.Verify(r => r.RevokeAllActiveAsync(target.Id, It.IsAny<CancellationToken>()), Times.Once,
            "a banned player's sessions are killed immediately");
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "PlayerBanned"), It.IsAny<CancellationToken>()), Times.Once);
        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p => p.Type == EmailType.ModerationAction),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once,
            "every punitive action raises a ModerationAction operator email");
    }

    // Northstar §6 reserves PERMANENT bans to Admins. A Moderator asking for one (which is what an
    // omitted duration means, including from an older client) must be refused and must change nothing.
    [Fact]
    public async Task BanPlayerAsync_ModeratorAskingForAPermanentBan_IsRefused_AndChangesNothing()
    {
        var (service, players, tokens, auditLog, emails) = BuildServiceEx();
        var actor  = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);
        var target = MakePlayer("baddie");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.BanPlayerAsync(actor.Id, "baddie", "cheating");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("admin");
        target.IsBanned.Should().BeFalse();
        players.Verify(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        auditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        emails.Verify(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Temporary bans ── northstar §6 gives a Moderator "temporary bans up to 3 days". That split was
    // unenforceable until BannedUntil existed, which is why banning was Admin-only in the interim.

    [Fact]
    public async Task BanPlayerAsync_ModeratorWithinThreeDays_Succeeds_AndSetsAnExpiry()
    {
        var (service, players, tokens, auditLog, emails) = BuildServiceEx();
        var actor  = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);
        var target = MakePlayer("baddie");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tokens.Setup(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.BanPlayerAsync(actor.Id, "baddie", "spam", durationDays: 3);

        result.Success.Should().BeTrue(result.FailureReason);
        target.IsBanned.Should().BeTrue();
        target.BannedUntil.Should().NotBeNull("a moderator's ban must be dated, never permanent");
        target.BannedUntil!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(3), TimeSpan.FromMinutes(1));
        tokens.Verify(r => r.RevokeAllActiveAsync(target.Id, It.IsAny<CancellationToken>()), Times.Once);
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "PlayerBanned" && l.ResultSummary!.Contains("duration=3d")),
            It.IsAny<CancellationToken>()), Times.Once,
            "the dispute trail must record how long the ban was for");
        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(pl => pl.Type == EmailType.ModerationAction),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BanPlayerAsync_ModeratorBeyondThreeDays_IsRefused_AndChangesNothing()
    {
        var (service, players, tokens, auditLog, _) = BuildServiceEx();
        var actor  = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);
        var target = MakePlayer("baddie");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.BanPlayerAsync(actor.Id, "baddie", "spam", durationDays: 4);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("3 days");
        target.IsBanned.Should().BeFalse();
        players.Verify(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        auditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BanPlayerAsync_AdminMayBanForLongerThanAModerator()
    {
        var (service, players, tokens, _, _) = BuildServiceEx();
        var actor  = MakeAdmin("boss");
        var target = MakePlayer("baddie");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tokens.Setup(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.BanPlayerAsync(actor.Id, "baddie", "repeat offender", durationDays: 90);

        result.Success.Should().BeTrue(result.FailureReason);
        target.BannedUntil!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task BanPlayerAsync_BeyondTheDatedCeiling_IsRefused()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor = MakeAdmin("boss");
        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);

        var result = await service.BanPlayerAsync(actor.Id, "baddie", "forever-ish", durationDays: 4000);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("permanent", "past a decade the honest answer is a permanent ban");
    }

    [Fact]
    public async Task BanPlayerAsync_NonPositiveDuration_IsRefused()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor = MakeAdmin("boss");
        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);

        var result = await service.BanPlayerAsync(actor.Id, "baddie", "oops", durationDays: 0);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("positive");
    }

    // A moderator may reverse the CLASS of ban they may issue — otherwise the §6 split is bypassed from
    // the other direction, and a moderator cannot even undo their own mistake.

    [Fact]
    public async Task UnbanPlayerAsync_ModeratorMayLiftATemporaryBan()
    {
        var (service, players, _, auditLog, _) = BuildServiceEx();
        var actor  = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);
        var target = MakePlayer("baddie");
        target.Ban("spam", DateTimeOffset.UtcNow.AddDays(2));

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.UnbanPlayerAsync(actor.Id, "baddie", "appeal upheld");

        result.Success.Should().BeTrue(result.FailureReason);
        target.IsBanned.Should().BeFalse();
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "PlayerUnbanned"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnbanPlayerAsync_ModeratorMayNotLiftAPermanentBan()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor  = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);
        var target = MakePlayer("baddie");
        target.Ban("cheating");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.UnbanPlayerAsync(actor.Id, "baddie", "feeling generous");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("permanent");
        target.IsBanned.Should().BeTrue("a refused unban must not lift the ban");
    }

    // An expired temporary ban is already over, so there is nothing left to lift.
    [Fact]
    public async Task UnbanPlayerAsync_ExpiredTemporaryBan_ReportsNotBanned()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor  = MakeAdmin("boss");
        var target = MakePlayer("baddie");
        target.Ban("spam", DateTimeOffset.UtcNow.AddSeconds(-1));

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.UnbanPlayerAsync(actor.Id, "baddie", "housekeeping");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not banned");
    }

    // §6 forbids reasonless punishment. The validator was the ONLY guard; this pins the service-level one.
    [Fact]
    public async Task BanPlayerAsync_BlankReason_IsRefused()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor = MakeAdmin("boss");
        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);

        var result = await service.BanPlayerAsync(actor.Id, "baddie", "   ");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("reason");
    }

    [Fact]
    public async Task UnbanPlayerAsync_ValidAdmin_LiftsBan_Audits_AndEmails()
    {
        var (service, players, _, auditLog, emails) = BuildServiceEx();
        var actor  = MakeAdmin("boss");
        var target = MakePlayer("baddie");
        target.Ban("cheating");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.UnbanPlayerAsync(actor.Id, "baddie", "appeal upheld");

        result.Success.Should().BeTrue();
        target.IsBanned.Should().BeFalse();
        target.BanReason.Should().BeNull();
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "PlayerUnbanned"), It.IsAny<CancellationToken>()), Times.Once);
        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p => p.Type == EmailType.ModerationAction),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnbanPlayerAsync_ModeratorActor_IsRefused()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor  = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);
        var target = MakePlayer("baddie");
        target.Ban("cheating");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("baddie", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.UnbanPlayerAsync(actor.Id, "baddie", "appeal upheld");

        result.Success.Should().BeFalse();
        target.IsBanned.Should().BeTrue("a refused unban must not lift the ban");
    }

    [Fact]
    public async Task UnbanPlayerAsync_NotBanned_ReturnsFail()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor  = MakeAdmin("boss");
        var target = MakePlayer("innocent");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("innocent", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.UnbanPlayerAsync(actor.Id, "innocent", "n/a");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not banned");
    }

    // The service-level cap must hold for any caller that bypasses the controller's validator.
    [Fact]
    public async Task MutePlayerAsync_DurationBeyondCap_IsRefused()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);
        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);

        var result = await service.MutePlayerAsync(actor.Id, "baddie", 60 * 24 * 31, "spam");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("exceed");
    }

    [Fact]
    public async Task BanPlayerAsync_TargetIsAdmin_Fails_NoEmail()
    {
        var (service, players, _, _, emails) = BuildServiceEx();
        var actor  = MakeAdmin("actor");
        var target = MakeAdmin("other-admin");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("other-admin", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.BanPlayerAsync(actor.Id, "other-admin", "x");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("admin");
        emails.Verify(e => e.QueueAsync(
            It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Moderation polish (audit ticket) — a Moderator cannot ban/mute fellow staff; an Admin can.

    [Fact]
    public async Task BanPlayerAsync_ModeratorTargetsModerator_Fails()
    {
        var (service, players, _, _, emails) = BuildServiceEx();
        var actor  = MakePlayer("mod1", PlayerRoles.Player | PlayerRoles.Moderator);
        var target = MakePlayer("mod2", PlayerRoles.Player | PlayerRoles.Moderator);

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("mod2", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.BanPlayerAsync(actor.Id, "mod2", "grudge");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("admin", "only an admin may act on staff");
        target.IsBanned.Should().BeFalse();
        emails.Verify(e => e.QueueAsync(
            It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MutePlayerAsync_ModeratorTargetsDeveloper_Fails()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor  = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);
        var target = MakePlayer("dev", PlayerRoles.Player | PlayerRoles.Developer);

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("dev", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.MutePlayerAsync(actor.Id, "dev", 30, "x");

        result.Success.Should().BeFalse();
        target.IsMuted.Should().BeFalse();
    }

    [Fact]
    public async Task BanPlayerAsync_AdminTargetsModerator_Succeeds()
    {
        var (service, players, tokens, _, _) = BuildServiceEx();
        var actor  = MakeAdmin("admin");
        var target = MakePlayer("mod", PlayerRoles.Player | PlayerRoles.Moderator);

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("mod", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tokens.Setup(r => r.RevokeAllActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.BanPlayerAsync(actor.Id, "mod", "abuse of power");

        result.Success.Should().BeTrue();
        target.IsBanned.Should().BeTrue();
    }

    [Fact]
    public async Task MutePlayerAsync_ValidModerator_SetsMute_Audits_AndEmails()
    {
        var (service, players, _, auditLog, emails) = BuildServiceEx();
        var actor  = MakeAdmin();
        var target = MakePlayer("chatty");

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("chatty", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.MutePlayerAsync(actor.Id, "chatty", 30, "spam");

        result.Success.Should().BeTrue();
        target.IsMuted.Should().BeTrue();
        target.MuteExpiresAt.Should().NotBeNull();
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "PlayerMuted"), It.IsAny<CancellationToken>()), Times.Once);
        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p => p.Type == EmailType.ModerationAction),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MutePlayerAsync_NonModeratorActor_Fails()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor = MakePlayer("regular"); // plain player, no mod/admin

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);

        var result = await service.MutePlayerAsync(actor.Id, "x", 10, "y");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not a moderator");
    }

    [Fact]
    public async Task MutePlayerAsync_NonPositiveDuration_Fails()
    {
        var (service, _, _, _, _) = BuildServiceEx();

        var result = await service.MutePlayerAsync(Guid.Empty, "x", 0, "y");

        result.Success.Should().BeFalse("a non-positive mute duration is rejected");
    }

    [Fact]
    public async Task UnmutePlayerAsync_ClearsMute()
    {
        var (service, players, _, _, _) = BuildServiceEx();
        var actor  = MakeAdmin();
        var target = MakePlayer("muted");
        target.Mute(DateTimeOffset.UtcNow.AddHours(1));

        players.Setup(r => r.FindByIdAsync(actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        players.Setup(r => r.FindByUsernameAsync("muted", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        players.Setup(r => r.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.UnmutePlayerAsync(actor.Id, "muted");

        result.Success.Should().BeTrue();
        target.IsMuted.Should().BeFalse();
        target.MuteExpiresAt.Should().BeNull();
    }
}
