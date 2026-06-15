using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ROTA.Api.Controllers;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;
using System.Security.Claims;

namespace ROTA.UnitTests.Controllers;

// BETA — unit tests for LeaderboardController (Slice 3).
// Thin-controller tests: verify correct HTTP status codes and delegation to ILeaderboardService.
public class LeaderboardControllerTests
{
    private static LeaderboardController BuildController(Mock<ILeaderboardService> svc)
    {
        var controller = new LeaderboardController(svc.Object);
        // Inject a fake JWT sub claim so GetPlayerId() works.
        var playerId = Guid.NewGuid();
        var claims   = new[] { new Claim("sub", playerId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims)),
            },
        };
        return controller;
    }

    private static LeaderboardPageResponse EmptyPage(string board = "DamageDealt") => new()
    {
        Board       = board,
        Period      = "Weekly",
        PeriodKey   = "week:2026-W23",
        Page        = 1,
        PageSize    = 200,
        TotalRanked = 0,
        Entries     = new List<LeaderboardEntryDto>(),
        You         = null,
    };

    [Fact]
    public async Task GetBoards_Returns200WithSummaries()
    {
        var svc = new Mock<ILeaderboardService>();
        svc.Setup(s => s.GetBoardsAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(new List<LeaderboardSummary>
           {
               new() { Board = "DamageDealt", Title = "Top Raiders",
                        Periods = new List<string> { "Weekly" }, CurrentPeriodKey = "week:2026-W23" },
           });

        var ctrl   = BuildController(svc);
        var result = await ctrl.GetBoards() as OkObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetBoard_ValidCombo_Returns200()
    {
        var svc = new Mock<ILeaderboardService>();
        svc.Setup(s => s.GetPageAsync(
                LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly,
                null, 1, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LeaderboardPageResult.Ok(EmptyPage()));

        var ctrl   = BuildController(svc);
        var result = await ctrl.GetBoard("DamageDealt", "Weekly", null, 1) as OkObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetBoard_UnknownBoard_Returns400()
    {
        var svc    = new Mock<ILeaderboardService>();
        var ctrl   = BuildController(svc);

        var result = await ctrl.GetBoard("NotABoard", "Weekly", null, 1) as BadRequestObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetBoard_UnknownPeriod_Returns400()
    {
        var svc  = new Mock<ILeaderboardService>();
        var ctrl = BuildController(svc);

        // "NotAPeriod" is not a valid LeaderboardPeriod enum name.
        var result = await ctrl.GetBoard("DamageDealt", "NotAPeriod", null, 1) as BadRequestObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetBoard_ServiceValidationFailure_Returns400()
    {
        var svc = new Mock<ILeaderboardService>();
        svc.Setup(s => s.GetPageAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LeaderboardPageResult.Fail("Board 'StatAttack' does not support period 'Weekly'."));

        var ctrl   = BuildController(svc);
        var result = await ctrl.GetBoard("StatAttack", "Weekly", null, 1) as BadRequestObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetBoard_ForwardsPageParameterToService()
    {
        var svc        = new Mock<ILeaderboardService>();
        int capturedPage = 0;

        svc.Setup(s => s.GetPageAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
           .Callback<LeaderboardBoard, LeaderboardPeriod, string?, int, Guid, CancellationToken>(
               (_, _, _, pg, _, _) => capturedPage = pg)
           .ReturnsAsync(LeaderboardPageResult.Ok(EmptyPage()));

        var ctrl = BuildController(svc);
        await ctrl.GetBoard("DamageDealt", "Weekly", null, 3);

        capturedPage.Should().Be(3);
    }

    [Fact]
    public async Task GetBoard_ForwardsPeriodKeyToService()
    {
        var svc           = new Mock<ILeaderboardService>();
        string? capturedKey = null;

        svc.Setup(s => s.GetPageAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
           .Callback<LeaderboardBoard, LeaderboardPeriod, string?, int, Guid, CancellationToken>(
               (_, _, pk, _, _, _) => capturedKey = pk)
           .ReturnsAsync(LeaderboardPageResult.Ok(EmptyPage()));

        var ctrl = BuildController(svc);
        await ctrl.GetBoard("DamageDealt", "Weekly", "week:2026-W20", 1);

        capturedKey.Should().Be("week:2026-W20");
    }
}
