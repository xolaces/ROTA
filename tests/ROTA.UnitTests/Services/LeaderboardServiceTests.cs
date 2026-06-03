using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

// BETA — unit tests for LeaderboardService (Slice 3).
// The repo mock returns EligibleLeaderboardEntry / CallerRankEntry objects directly;
// SQL correctness is covered by integration tests (LeaderboardEligibilityTests).
public class LeaderboardServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (LeaderboardService service,
                    Mock<ILeaderboardEntryRepository> repo,
                    Mock<IPlayerRepository> players,
                    Mock<IPeriodKeyResolver> resolver)
        Build(int pageSize = 200, int minLevel = 20, bool excludeAdmins = true)
    {
        var repo     = new Mock<ILeaderboardEntryRepository>();
        var players  = new Mock<IPlayerRepository>();
        var resolver = new Mock<IPeriodKeyResolver>();

        // Default resolver: return a sensible key for any period
        resolver.Setup(r => r.Resolve(It.IsAny<DateTimeOffset>(), LeaderboardPeriod.Weekly))
            .Returns("week:2026-W23");
        resolver.Setup(r => r.Resolve(It.IsAny<DateTimeOffset>(), LeaderboardPeriod.Monthly))
            .Returns("month:2026-06");
        resolver.Setup(r => r.Resolve(It.IsAny<DateTimeOffset>(), LeaderboardPeriod.Daily))
            .Returns("day:2026-06-02");
        resolver.Setup(r => r.Resolve(It.IsAny<DateTimeOffset>(), LeaderboardPeriod.Live))
            .Returns("live");

        var cfg = Options.Create(new LeaderboardConfig
        {
            PageSize      = pageSize,
            MinLevel      = minLevel,
            ExcludeAdmins = excludeAdmins,
        });

        var service = new LeaderboardService(repo.Object, players.Object, resolver.Object, cfg);
        return (service, repo, players, resolver);
    }

    // Helper: create a list of EligibleLeaderboardEntry with descending values.
    private static List<EligibleLeaderboardEntry> MakeEntries(int count, long startValue = 1000)
        => Enumerable.Range(0, count).Select(i => new EligibleLeaderboardEntry
        {
            PlayerId       = Guid.NewGuid(),
            Value          = startValue - i * 100,
            LastProgressAt = DateTimeOffset.UtcNow.AddMinutes(-i),
            DisplayName    = $"Player{i + 1}",
            Username       = $"player{i + 1}",
        }).ToList();

    // ── GetBoardsAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBoardsAsync_ReturnsAllSixBoards()
    {
        var (service, _, _, _) = Build();

        var boards = await service.GetBoardsAsync();

        boards.Should().HaveCount(6);
        boards.Select(b => b.Board).Should().Contain(new[]
        {
            "StatAttack", "StatDefense", "StatDiscernment",
            "EnergySpent", "DamageDealt", "LargestHit",
        });
    }

    [Fact]
    public async Task GetBoardsAsync_StatBoardsHaveLivePeriod()
    {
        var (service, _, _, _) = Build();

        var boards = await service.GetBoardsAsync();

        foreach (var statBoard in boards.Where(b =>
            b.Board is "StatAttack" or "StatDefense" or "StatDiscernment"))
        {
            statBoard.Periods.Should().BeEquivalentTo(new[] { "Live" });
            statBoard.CurrentPeriodKey.Should().Be("live");
        }
    }

    [Fact]
    public async Task GetBoardsAsync_LargestHitHasDailyPeriod()
    {
        var (service, _, _, _) = Build();

        var boards = await service.GetBoardsAsync();
        var hit = boards.Single(b => b.Board == "LargestHit");

        hit.Periods.Should().BeEquivalentTo(new[] { "Daily" });
        hit.CurrentPeriodKey.Should().Be("day:2026-06-02");
    }

    [Fact]
    public async Task GetBoardsAsync_EnergyAndDamageSupportWeeklyAndMonthly()
    {
        var (service, _, _, _) = Build();

        var boards = await service.GetBoardsAsync();

        foreach (var b in boards.Where(b => b.Board is "EnergySpent" or "DamageDealt"))
        {
            b.Periods.Should().Contain("Weekly");
            b.Periods.Should().Contain("Monthly");
        }
    }

    // ── GetPageAsync — validation failures → 400 ─────────────────────────────

    [Fact]
    public async Task GetPageAsync_PageLessThanOne_ReturnsFail()
    {
        var (service, _, _, _) = Build();

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, null, page: 0, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Page");
    }

    [Fact]
    public async Task GetPageAsync_StatBoardWithWeeklyPeriod_ReturnsFail()
    {
        var (service, _, _, _) = Build();

        var result = await service.GetPageAsync(
            LeaderboardBoard.StatAttack, LeaderboardPeriod.Weekly, null, page: 1, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("StatAttack");
    }

    [Fact]
    public async Task GetPageAsync_DamageDealtWithLivePeriod_ReturnsFail()
    {
        var (service, _, _, _) = Build();

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Live, null, page: 1, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("DamageDealt");
    }

    [Fact]
    public async Task GetPageAsync_LargestHitWithWeeklyPeriod_ReturnsFail()
    {
        var (service, _, _, _) = Build();

        var result = await service.GetPageAsync(
            LeaderboardBoard.LargestHit, LeaderboardPeriod.Weekly, null, page: 1, Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetPageAsync_MismatchedPeriodKeyFormat_ReturnsFail()
    {
        var (service, _, _, _) = Build();
        // A "month:" key supplied on a Daily board is a format mismatch.
        var result = await service.GetPageAsync(
            LeaderboardBoard.LargestHit, LeaderboardPeriod.Daily,
            periodKey: "month:2026-06", page: 1, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("month:2026-06");
    }

    // ── GetPageAsync — successful reads ──────────────────────────────────────

    [Fact]
    public async Task GetPageAsync_ReturnsRanksContiguousStartingAt1()
    {
        var (service, repo, _, _) = Build(pageSize: 200);
        var callerId = Guid.NewGuid();
        var entries  = MakeEntries(5);

        repo.Setup(r => r.GetEligiblePageAsync(
                LeaderboardBoard.DamageDealt, "week:2026-W23", 1, 200,
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        repo.Setup(r => r.CountEligibleAsync(
                LeaderboardBoard.DamageDealt, "week:2026-W23",
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        repo.Setup(r => r.GetCallerRankAsync(
                callerId, LeaderboardBoard.DamageDealt, "week:2026-W23",
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallerRankEntry?)null);

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, null, 1, callerId);

        result.Success.Should().BeTrue();
        var page = result.Page!;
        page.Entries.Should().HaveCount(5);
        page.Entries.Select(e => e.Rank).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task GetPageAsync_Page2_RanksStartAtOffset()
    {
        var (service, repo, _, _) = Build(pageSize: 3);
        var callerId = Guid.NewGuid();
        // Page 2 of a page-3 board should have ranks 4, 5, 6.
        var entries = MakeEntries(3, startValue: 700);

        repo.Setup(r => r.GetEligiblePageAsync(
                LeaderboardBoard.DamageDealt, "week:2026-W23", 2, 3,
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        repo.Setup(r => r.CountEligibleAsync(
                LeaderboardBoard.DamageDealt, "week:2026-W23",
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(6);

        repo.Setup(r => r.GetCallerRankAsync(
                callerId, LeaderboardBoard.DamageDealt, "week:2026-W23",
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallerRankEntry?)null);

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, null, 2, callerId);

        result.Success.Should().BeTrue();
        result.Page!.Entries.Select(e => e.Rank).Should().Equal(4, 5, 6);
    }

    [Fact]
    public async Task GetPageAsync_CallerOffPage_YouIncludesCorrectRank()
    {
        var (service, repo, players, _) = Build(pageSize: 3);
        var callerId    = Guid.NewGuid();
        var callerEntry = new CallerRankEntry { PlayerId = callerId, Value = 50L, Rank = 7 };
        var entries     = MakeEntries(3, startValue: 1000);

        repo.Setup(r => r.GetEligiblePageAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<string>(), 1, 3,
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        repo.Setup(r => r.CountEligibleAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        repo.Setup(r => r.GetCallerRankAsync(
                callerId, It.IsAny<LeaderboardBoard>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEntry);

        // Caller not on the page — service will call IPlayerRepository.FindByIdAsync
        var callerPlayer = Player.Create("calleruser", "caller@rota.test", "hash");
        players.Setup(p => p.FindByIdAsync(callerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(callerPlayer);

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, null, 1, callerId);

        result.Success.Should().BeTrue();
        var you = result.Page!.You;
        you.Should().NotBeNull();
        you!.Rank.Should().Be(7, "caller's rank must come from GetCallerRankAsync, not the page position");
        you.Value.Should().Be(50L);
        you.PlayerId.Should().Be(callerId);
    }

    [Fact]
    public async Task GetPageAsync_EmptyBoard_ReturnsEmptyEntriesAndNullYou()
    {
        var (service, repo, _, _) = Build();
        var callerId = Guid.NewGuid();

        repo.Setup(r => r.GetEligiblePageAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EligibleLeaderboardEntry>());

        repo.Setup(r => r.CountEligibleAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        repo.Setup(r => r.GetCallerRankAsync(
                callerId, It.IsAny<LeaderboardBoard>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallerRankEntry?)null);

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, null, 1, callerId);

        result.Success.Should().BeTrue();
        result.Page!.Entries.Should().BeEmpty();
        result.Page.TotalRanked.Should().Be(0);
        result.Page.You.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_CallerHasNoEntry_YouIsNull()
    {
        var (service, repo, _, _) = Build();
        var callerId = Guid.NewGuid();
        var entries  = MakeEntries(3);

        repo.Setup(r => r.GetEligiblePageAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        repo.Setup(r => r.CountEligibleAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Caller has no entry on this board
        repo.Setup(r => r.GetCallerRankAsync(
                callerId, It.IsAny<LeaderboardBoard>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallerRankEntry?)null);

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, null, 1, callerId);

        result.Success.Should().BeTrue();
        result.Page!.You.Should().BeNull("caller has no entry on this board");
    }

    [Fact]
    public async Task GetPageAsync_ValidPastPeriodKey_ReturnsPage()
    {
        var (service, repo, _, _) = Build();
        var callerId   = Guid.NewGuid();
        const string pastKey = "week:2026-W20";
        var entries = MakeEntries(2);

        repo.Setup(r => r.GetEligiblePageAsync(
                LeaderboardBoard.DamageDealt, pastKey, 1, 200,
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        repo.Setup(r => r.CountEligibleAsync(
                LeaderboardBoard.DamageDealt, pastKey,
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        repo.Setup(r => r.GetCallerRankAsync(
                callerId, LeaderboardBoard.DamageDealt, pastKey,
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallerRankEntry?)null);

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, pastKey, 1, callerId);

        result.Success.Should().BeTrue();
        result.Page!.PeriodKey.Should().Be(pastKey);
        result.Page.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPageAsync_DisplayName_FallsBackToUsername()
    {
        var (service, repo, _, _) = Build();
        var callerId = Guid.NewGuid();

        // Entry with an empty DisplayName — Username should be used.
        var entries = new List<EligibleLeaderboardEntry>
        {
            new() { PlayerId = Guid.NewGuid(), Value = 500L,
                    LastProgressAt = DateTimeOffset.UtcNow,
                    DisplayName = "", Username = "fallback_user" },
        };

        repo.Setup(r => r.GetEligiblePageAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        repo.Setup(r => r.CountEligibleAsync(
                It.IsAny<LeaderboardBoard>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        repo.Setup(r => r.GetCallerRankAsync(
                callerId, It.IsAny<LeaderboardBoard>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallerRankEntry?)null);

        var result = await service.GetPageAsync(
            LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, null, 1, callerId);

        result.Page!.Entries[0].DisplayName.Should().Be("fallback_user");
    }
}
