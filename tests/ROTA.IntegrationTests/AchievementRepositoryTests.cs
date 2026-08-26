using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// TICKET 46 — Postgres-backed tests for the raw ON CONFLICT increment / absolute SetCounter (can't be
// unit-tested) and the unique-violation-idempotent award ledger (the gem-ledger discipline).
public class AchievementRepositoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_achievement_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync(); // includes AddAchievementSystem
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new RotaDbContext(options);
    }

    private async Task<Guid> SeedPlayerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"a_{suffix}", $"{suffix}@test.dev", "hash");
        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    [Fact]
    public async Task Increment_CreatesThenAccumulates()
    {
        var pid = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new AchievementProgressRepository(db);

        await repo.IncrementAsync(pid, "ach_raids_10", 6);
        await repo.IncrementAsync(pid, "ach_raids_10", 4);

        var rows = await repo.GetForPlayerAsync(pid);
        rows.Should().ContainSingle(p => p.AchievementId == "ach_raids_10")
            .Which.ProgressValue.Should().Be(10, "ON CONFLICT accumulates: 6 + 4");
    }

    [Fact]
    public async Task SetCounter_WritesAbsolute_AndReGrantDoesNotInflate()
    {
        var pid = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new AchievementProgressRepository(db);

        await repo.SetCounterAsync(pid, "ach_gear_25", 20);
        await repo.SetCounterAsync(pid, "ach_gear_25", 22); // a re-grant recount — absolute, not additive

        var rows = await repo.GetForPlayerAsync(pid);
        rows.Single(p => p.AchievementId == "ach_gear_25").ProgressValue.Should().Be(22,
            "absolute SetCounter overwrites — it never accumulates");
    }

    [Fact]
    public async Task AwardLedger_CreateTwiceSameAchievement_CreditsOnce()
    {
        var pid = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new AchievementAwardRepository(db);

        var refId = $"achievement:{pid}:ach_raids_10";
        var first  = await repo.CreateAsync(AchievementAward.Create(pid, "ach_raids_10", 10, refId));
        var second = await repo.CreateAsync(AchievementAward.Create(pid, "ach_raids_10", 10, refId));

        first.Should().BeTrue("the first award inserts");
        second.Should().BeFalse("the unique (player, achievement) index rejects the duplicate");
        (await repo.GetTotalPointsAsync(pid)).Should().Be(10, "AP is SUMMED and the dup was rejected");
        (await repo.ReferenceExistsAsync(pid, refId)).Should().BeTrue();
    }

    [Fact]
    public async Task AwardLedger_TotalPoints_SumsAcrossAchievements()
    {
        var pid = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new AchievementAwardRepository(db);

        await repo.CreateAsync(AchievementAward.Create(pid, "ach_raids_10", 10, $"achievement:{pid}:ach_raids_10"));
        await repo.CreateAsync(AchievementAward.Create(pid, "ach_quest_nodes_50", 15, $"achievement:{pid}:ach_quest_nodes_50"));

        (await repo.GetTotalPointsAsync(pid)).Should().Be(25);
    }
}
