using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

// BETA (System 16 Slice 3) — unit tests for GauntletScoringService: the service is a thin orchestration
// over IGauntletEntryRepository (all ranking/ordering/isolation lives in SQL, covered by the
// integration tests). These verify the service wires the repo correctly: the leaderboard assembles
// page + caller + total, the caller-rank query is league-scoped, and the score/rank passthroughs hold.
public class GauntletScoringServiceTests
{
    private sealed class Bundle
    {
        public Mock<IGauntletEntryRepository> Entries = new();
        public GauntletConfig Config = new();

        public GauntletScoringService Build()
            => new(Entries.Object, Options.Create(Config));
    }

    // ── GetLeaderboardAsync assembly ────────────────────────────────────────────

    [Fact]
    public async Task GetLeaderboard_AssemblesPage_CallerStanding_AndTotalRanked()
    {
        var b = new Bundle();
        b.Config.LeaderboardPageSize = 200;
        var eventId = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        var rows = new List<GauntletLeaderboardRow>
        {
            new() { Rank = 1, PlayerId = Guid.NewGuid(), DisplayName = "Top",    Score = 5000 },
            new() { Rank = 2, PlayerId = callerId,       DisplayName = "Caller", Score = 3000 },
        };
        b.Entries.Setup(r => r.GetLeaderboardPageAsync(
                eventId, GauntletLeague.Wyrm, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        b.Entries.Setup(r => r.CountRankedAsync(
                eventId, GauntletLeague.Wyrm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        b.Entries.Setup(r => r.GetRankAndScoreAsync(
                eventId, callerId, GauntletLeague.Wyrm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GauntletRankScore { Rank = 2, Score = 3000 });

        var result = await b.Build().GetLeaderboardAsync(eventId, GauntletLeague.Wyrm, callerId);

        result.League.Should().Be("Wyrm");
        result.Entries.Should().HaveCount(2);
        result.Entries[0].Rank.Should().Be(1);
        result.Entries[0].DisplayName.Should().Be("Top");
        result.YourRank.Should().Be(2);
        result.YourScore.Should().Be(3000);
        result.TotalRanked.Should().Be(42);
    }

    [Fact]
    public async Task GetLeaderboard_UsesConfiguredPageSize_AsTake()
    {
        var b = new Bundle();
        b.Config.LeaderboardPageSize = 50; // non-default, must be passed as the take
        var eventId = Guid.NewGuid();
        b.Entries.Setup(r => r.GetLeaderboardPageAsync(
                It.IsAny<Guid>(), It.IsAny<GauntletLeague>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletLeaderboardRow>());
        b.Entries.Setup(r => r.CountRankedAsync(
                It.IsAny<Guid>(), It.IsAny<GauntletLeague>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await b.Build().GetLeaderboardAsync(eventId, GauntletLeague.Whelpling, Guid.NewGuid());

        b.Entries.Verify(r => r.GetLeaderboardPageAsync(
            eventId, GauntletLeague.Whelpling, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLeaderboard_NullCallerStanding_LeavesYourRankAndScoreNull()
    {
        var b = new Bundle();
        var eventId = Guid.NewGuid();
        b.Entries.Setup(r => r.GetLeaderboardPageAsync(
                It.IsAny<Guid>(), It.IsAny<GauntletLeague>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletLeaderboardRow>());
        b.Entries.Setup(r => r.CountRankedAsync(
                It.IsAny<Guid>(), It.IsAny<GauntletLeague>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        b.Entries.Setup(r => r.GetRankAndScoreAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<GauntletLeague?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletRankScore?)null); // caller has no entry in this league

        var result = await b.Build().GetLeaderboardAsync(eventId, GauntletLeague.Dragon, Guid.NewGuid());

        result.Entries.Should().BeEmpty();
        result.YourRank.Should().BeNull();
        result.YourScore.Should().BeNull();
        result.TotalRanked.Should().Be(0);
    }

    [Fact]
    public async Task GetLeaderboard_ScopesCallerRankQuery_ToRequestedLeague()
    {
        // The caller-standing lookup MUST pass the requested league so a player whose locked league
        // differs does not surface a rank on the wrong board.
        var b = new Bundle();
        var eventId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        b.Entries.Setup(r => r.GetLeaderboardPageAsync(
                It.IsAny<Guid>(), It.IsAny<GauntletLeague>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletLeaderboardRow>());
        b.Entries.Setup(r => r.CountRankedAsync(
                It.IsAny<Guid>(), It.IsAny<GauntletLeague>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await b.Build().GetLeaderboardAsync(eventId, GauntletLeague.Whelpling, callerId);

        b.Entries.Verify(r => r.GetRankAndScoreAsync(
            eventId, callerId, GauntletLeague.Whelpling, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Passthroughs ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateScore_DelegatesToIncrementScore_WithSameArgs()
    {
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var eventId  = Guid.NewGuid();
        var hitAt    = DateTimeOffset.UtcNow;

        await b.Build().UpdateScoreAsync(playerId, eventId, 750L, hitAt);

        b.Entries.Verify(r => r.IncrementScoreAsync(
            eventId, playerId, 750L, hitAt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecomputeRanks_DelegatesToRepo()
    {
        var b = new Bundle();
        var eventId = Guid.NewGuid();

        await b.Build().RecomputeRanksAsync(eventId);

        b.Entries.Verify(r => r.RecomputeRanksAsync(eventId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
