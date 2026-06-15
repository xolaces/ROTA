using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

// BETA — unit tests for LeaderboardService.SnapshotStatBoardAsync (Slice 5).
// Repository SQL behaviour (SetValueAsync idempotency, last_progress_at preservation)
// is covered by integration tests (LeaderboardStatSnapshotIntegrationTests).
public class LeaderboardStatSnapshotTests
{
    private static (LeaderboardService service,
                    Mock<ILeaderboardEntryRepository> repo)
        Build(int minLevel = 20, bool excludeAdmins = true)
    {
        var repo     = new Mock<ILeaderboardEntryRepository>();
        var players  = new Mock<IPlayerRepository>();
        var resolver = new Mock<IPeriodKeyResolver>();

        resolver.Setup(r => r.Resolve(It.IsAny<DateTimeOffset>(), LeaderboardPeriod.Live))
            .Returns("live");

        var cfg = Options.Create(new LeaderboardConfig
        {
            PageSize      = 200,
            MinLevel      = minLevel,
            ExcludeAdmins = excludeAdmins,
        });

        var service = new LeaderboardService(repo.Object, players.Object, resolver.Object, cfg);
        return (service, repo);
    }

    [Fact]
    public async Task Snapshot_CallsSetValueAsync_ThreeTimesPerEligiblePlayer()
    {
        var (service, repo) = Build();

        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();

        var snapshots = new List<EligibleStatSnapshot>
        {
            new() { PlayerId = p1, BaseAttack = 50,  BaseDefense = 30, DiscernmentInvestment = 10 },
            new() { PlayerId = p2, BaseAttack = 80,  BaseDefense = 60, DiscernmentInvestment = 40 },
        };

        repo.Setup(r => r.GetEligibleStatSnapshotAsync(20, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        repo.Setup(r => r.SetValueAsync(
                It.IsAny<Guid>(), It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var count = await service.SnapshotStatBoardAsync();

        count.Should().Be(2, "two eligible players were returned by the repository");

        repo.Verify(r => r.SetValueAsync(
            It.IsAny<Guid>(), It.IsAny<LeaderboardBoard>(), LeaderboardPeriod.Live,
            "live", It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Exactly(6), "3 boards × 2 players = 6 SetValueAsync calls");
    }

    [Fact]
    public async Task Snapshot_PassesCorrectRawValues_ToEachBoard()
    {
        var (service, repo) = Build();

        var playerId = Guid.NewGuid();
        var snapshots = new List<EligibleStatSnapshot>
        {
            new() { PlayerId = playerId, BaseAttack = 75, BaseDefense = 45, DiscernmentInvestment = 20 },
        };

        repo.Setup(r => r.GetEligibleStatSnapshotAsync(20, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var calls = new List<(LeaderboardBoard Board, long Value)>();
        repo.Setup(r => r.SetValueAsync(
                It.IsAny<Guid>(), It.IsAny<LeaderboardBoard>(), LeaderboardPeriod.Live,
                "live", It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, LeaderboardBoard, LeaderboardPeriod, string, long, DateTimeOffset, CancellationToken>(
                (_, board, _, _, value, _, _) => calls.Add((board, value)))
            .Returns(Task.CompletedTask);

        await service.SnapshotStatBoardAsync();

        calls.Should().Contain((LeaderboardBoard.StatAttack,      75L),  "BaseAttack maps to StatAttack");
        calls.Should().Contain((LeaderboardBoard.StatDefense,     45L),  "BaseDefense maps to StatDefense");
        calls.Should().Contain((LeaderboardBoard.StatDiscernment, 20L),  "DiscernmentInvestment maps to StatDiscernment");
    }

    [Fact]
    public async Task Snapshot_IneligiblePlayersAbsent_NoSetValueCalled()
    {
        var (service, repo) = Build();

        repo.Setup(r => r.GetEligibleStatSnapshotAsync(20, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EligibleStatSnapshot>());

        var count = await service.SnapshotStatBoardAsync();

        count.Should().Be(0, "no eligible players");
        repo.Verify(r => r.SetValueAsync(
            It.IsAny<Guid>(), It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
            It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()),
            Times.Never, "no SetValueAsync calls when no eligible players");
    }

    [Fact]
    public async Task Snapshot_UsesPeriodKeyLive_ForAllBoards()
    {
        var (service, repo) = Build();

        var playerId = Guid.NewGuid();
        repo.Setup(r => r.GetEligibleStatSnapshotAsync(20, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EligibleStatSnapshot>
            {
                new() { PlayerId = playerId, BaseAttack = 1, BaseDefense = 1, DiscernmentInvestment = 1 },
            });

        var periodKeys = new List<string>();
        repo.Setup(r => r.SetValueAsync(
                It.IsAny<Guid>(), It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, LeaderboardBoard, LeaderboardPeriod, string, long, DateTimeOffset, CancellationToken>(
                (_, _, _, pk, _, _, _) => periodKeys.Add(pk))
            .Returns(Task.CompletedTask);

        await service.SnapshotStatBoardAsync();

        periodKeys.Should().AllBe("live", "Stat boards always use period_key = \"live\"");
    }

    [Fact]
    public async Task Snapshot_ForwardsConfigToRepo()
    {
        var (service, repo) = Build(minLevel: 30, excludeAdmins: false);

        repo.Setup(r => r.GetEligibleStatSnapshotAsync(30, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EligibleStatSnapshot>());

        await service.SnapshotStatBoardAsync();

        repo.Verify(r => r.GetEligibleStatSnapshotAsync(30, false, It.IsAny<CancellationToken>()),
            Times.Once, "minLevel=30 and excludeAdmins=false must be forwarded to the repository");
    }
}
