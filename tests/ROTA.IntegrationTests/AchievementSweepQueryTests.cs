using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace ROTA.IntegrationTests;

// The completion sweep fetches only what it can act on.
//
// THE COST THIS REMOVES. EvaluateCompletionsAsync runs on every quest attempt, plus login, equipment
// grants and profile reads, which makes it the most frequently executed query in the game — and the
// potion design assumes hundreds of quest clicks per pool drain. It used to call GetForPlayerAsync,
// which selects EVERY progress row for the player, and then skip the completed ones in memory.
//
// That cost never shrank. A row becomes completed and stays completed forever, so the wasted portion
// of every fetch grew monotonically for the life of an account — and it tripled outright when the
// clear ladders went from 163 definitions to 466 (f59e481).
//
// These measure the reduction rather than assert it. The row counts are exact; no timing is asserted,
// because a wall-clock threshold in a container on shared CI is a flake, not a measurement.
public class AchievementSweepQueryTests : IAsyncLifetime
{
    // The real committed roster: 7 authored + 26 zones x 9 rungs + 25 raids x 9 rungs.
    private const int RosterSize = 7 + (26 * 9) + (25 * 9);

    private readonly ITestOutputHelper _output;
    private PostgreSqlContainer _postgres = null!;

    public AchievementSweepQueryTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_sweepquery_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
        => new(new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    /// <summary>
    /// A player part-way through the roster: <paramref name="completed"/> of <paramref name="total"/>
    /// rows finished, the rest still in progress.
    /// </summary>
    private async Task<Guid> SeedProgressAsync(int total, int completed)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"sweep_{suffix}", $"{suffix}@test.dev", "hash");

        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();

        for (int i = 0; i < total; i++)
        {
            var row = AchievementProgress.Create(player.Id, $"ach_synthetic_{i}", initial: 10);
            if (i < completed) row.MarkComplete(DateTimeOffset.UtcNow);
            db.AchievementProgress.Add(row);
        }
        await db.SaveChangesAsync();
        return player.Id;
    }

    [Fact]
    public async Task TheSweepQuery_ReturnsOnlyIncompleteRows()
    {
        // Correctness first: the filter must be exact, or the sweep silently stops awarding.
        var playerId = await SeedProgressAsync(total: 100, completed: 70);

        await using var db = NewDbContext();
        var repo = new AchievementProgressRepository(db);

        var all        = await repo.GetForPlayerAsync(playerId);
        var incomplete = await repo.GetIncompleteForPlayerAsync(playerId);

        all.Should().HaveCount(100, "the overview still needs every row");
        incomplete.Should().HaveCount(30);
        incomplete.Should().OnlyContain(r => !r.IsCompleted);
        incomplete.Select(r => r.AchievementId).Should().BeEquivalentTo(
            all.Where(r => !r.IsCompleted).Select(r => r.AchievementId),
            "the filtered query and an in-memory filter must select the same set");
    }

    [Fact]
    public async Task TheReduction_GrowsAsAPlayerFinishesAchievements()
    {
        // The point of the change: the old fetch was flat at the roster size no matter how much the
        // player had finished, so the waste rose monotonically for the life of an account.
        _output.WriteLine($"roster = {RosterSize} definitions (7 authored + 26 zones x 9 + 25 raids x 9)");
        _output.WriteLine("| completed | old fetch | new fetch | rows avoided |");

        foreach (var completed in new[] { 0, 100, 300, RosterSize - 10, RosterSize })
        {
            var playerId = await SeedProgressAsync(RosterSize, completed);

            await using var db = NewDbContext();
            var repo = new AchievementProgressRepository(db);

            var oldCount = (await repo.GetForPlayerAsync(playerId)).Count;
            var newCount = (await repo.GetIncompleteForPlayerAsync(playerId)).Count;

            _output.WriteLine($"| {completed} | {oldCount} | {newCount} | {oldCount - newCount} |");

            oldCount.Should().Be(RosterSize, "the old fetch never shrank");
            newCount.Should().Be(RosterSize - completed);
        }
    }

    [Fact]
    public async Task AFullyCompletedRoster_FetchesNothing()
    {
        // The end state a long-lived account trends toward. It used to be the most expensive case;
        // it is now the cheapest, which is the right way round.
        var playerId = await SeedProgressAsync(RosterSize, completed: RosterSize);

        await using var db = NewDbContext();
        var repo = new AchievementProgressRepository(db);

        (await repo.GetForPlayerAsync(playerId)).Should().HaveCount(RosterSize);
        (await repo.GetIncompleteForPlayerAsync(playerId)).Should().BeEmpty(
            "a player with nothing left to complete should fetch nothing on every quest click");
    }
}
