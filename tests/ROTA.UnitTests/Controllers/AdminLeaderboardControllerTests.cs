using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ROTA.Api.Controllers;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;
using System.Security.Claims;

namespace ROTA.UnitTests.Controllers;

// BETA — unit tests for the AdminController.RefreshStatBoards endpoint (Slice 5).
// Tests: happy path returns count + audit write; non-admin actor → Forbid.
// The [AdminOnly] policy is enforced by the ASP.NET authorisation middleware at runtime;
// these tests exercise the controller action body — actor DB re-verify, service delegation,
// audit write, and response shape.
public class AdminLeaderboardControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (AdminController ctrl,
                    Mock<ILeaderboardService> leaderboardSvc,
                    Mock<IPlayerRepository> playerRepo,
                    Mock<IAuditLogRepository> auditRepo)
        Build(Guid? actorId = null)
    {
        var admin         = new Mock<IAdminService>();
        var betaKeys      = new Mock<IBetaKeyService>();
        var betaKeyRepo   = new Mock<IBetaKeyRepository>();
        var roleValidator = new Mock<IValidator<RoleChangeRequest>>();
        var genValidator  = new Mock<IValidator<GenerateBetaKeysRequest>>();
        var leaderboards  = new Mock<ILeaderboardService>();
        var players       = new Mock<IPlayerRepository>();
        var auditLog      = new Mock<IAuditLogRepository>();

        var ctrl = new AdminController(
            admin.Object, betaKeys.Object, betaKeyRepo.Object,
            roleValidator.Object, genValidator.Object,
            leaderboards.Object, players.Object, auditLog.Object);

        var id = actorId ?? Guid.NewGuid();
        var claims = new[] { new Claim("sub", id.ToString()) };
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims)),
            },
        };

        return (ctrl, leaderboards, players, auditLog);
    }

    private static Player MakeAdmin(Guid id)
    {
        // Create a player and grant Admin role so HasRole(Admin) returns true.
        var suffix = id.ToString("N")[..8];
        var player = Player.Create($"adm_{suffix}", $"{suffix}@admin.test", "hash");
        player.GrantRole(PlayerRoles.Admin);
        return player;
    }

    // ── Test 1: happy path — valid admin → 200 + count + audit written ────────

    [Fact]
    public async Task RefreshStatBoards_ValidAdmin_Returns200WithCount()
    {
        var actorId = Guid.NewGuid();
        var (ctrl, leaderboards, players, auditLog) = Build(actorId);

        var adminPlayer = MakeAdmin(actorId);
        players.Setup(p => p.FindByIdAsync(actorId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(adminPlayer);

        leaderboards.Setup(s => s.SnapshotStatBoardAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(42);

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>()))
                .Returns(Task.CompletedTask);

        var result = await ctrl.RefreshStatBoards();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<StatBoardRefreshResponse>().Subject;
        body.PlayersSnapshotted.Should().Be(42);
        body.SnapshotAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── Test 2: audit_log written on happy path ───────────────────────────────

    [Fact]
    public async Task RefreshStatBoards_ValidAdmin_WritesAuditLog()
    {
        var actorId = Guid.NewGuid();
        var (ctrl, leaderboards, players, auditLog) = Build(actorId);

        var adminPlayer = MakeAdmin(actorId);
        players.Setup(p => p.FindByIdAsync(actorId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(adminPlayer);

        leaderboards.Setup(s => s.SnapshotStatBoardAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(5);

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>()))
                .Returns(Task.CompletedTask);

        await ctrl.RefreshStatBoards();

        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(log => log.Action == "StatBoardRefreshed")),
            Times.Once, "audit_log must record the StatBoardRefreshed action");
    }

    // ── Test 3: actor not found in DB → Forbid ────────────────────────────────

    [Fact]
    public async Task RefreshStatBoards_ActorNotFound_ReturnsForbid()
    {
        var actorId = Guid.NewGuid();
        var (ctrl, _, players, _) = Build(actorId);

        players.Setup(p => p.FindByIdAsync(actorId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);

        var result = await ctrl.RefreshStatBoards();

        result.Should().BeOfType<ForbidResult>("actor not found → Forbid");
    }

    // ── Test 4: actor found but not Admin → Forbid ────────────────────────────

    [Fact]
    public async Task RefreshStatBoards_ActorIsNotAdmin_ReturnsForbid()
    {
        var actorId = Guid.NewGuid();
        var (ctrl, _, players, _) = Build(actorId);

        var suffix    = actorId.ToString("N")[..8];
        var nonAdmin  = Player.Create($"usr_{suffix}", $"{suffix}@user.test", "hash");
        // Do NOT grant Admin role → HasRole(Admin) = false.

        players.Setup(p => p.FindByIdAsync(actorId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(nonAdmin);

        var result = await ctrl.RefreshStatBoards();

        result.Should().BeOfType<ForbidResult>("non-admin actor → Forbid (DB re-verify rejects)");
    }
}
