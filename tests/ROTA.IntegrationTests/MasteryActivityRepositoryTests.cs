using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// System 22 Phase A Slice 4 — Postgres-backed tests for the raw ON CONFLICT increment (can't be
// unit-tested) and the idempotency event ledger.
public class MasteryActivityRepositoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_masteryactivity_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync(); // includes AddMasterySystem
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
        var player = Player.Create($"m_{suffix}", $"{suffix}@test.dev", "hash");
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
        var repo = new PlayerMasteryActivityRepository(db);

        await repo.IncrementAsync(pid, MasteryActivityType.RaidHit, 10);
        await repo.IncrementAsync(pid, MasteryActivityType.RaidHit, 5);

        var rows = await repo.GetForPlayerAsync(pid);
        rows.Should().ContainSingle(a => a.ActivityType == MasteryActivityType.RaidHit)
            .Which.Counter.Should().Be(15, "ON CONFLICT accumulates: 10 + 5");
    }

    [Fact]
    public async Task Increment_SeparateTypes_KeepSeparateRows()
    {
        var pid = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new PlayerMasteryActivityRepository(db);

        await repo.IncrementAsync(pid, MasteryActivityType.RaidHit, 3);
        await repo.IncrementAsync(pid, MasteryActivityType.RaidKill, 1);
        await repo.IncrementAsync(pid, MasteryActivityType.GuildRaidContribution, 1_000_000);

        var rows = await repo.GetForPlayerAsync(pid);
        rows.Should().HaveCount(3);
        rows.Single(a => a.ActivityType == MasteryActivityType.RaidHit).Counter.Should().Be(3);
        rows.Single(a => a.ActivityType == MasteryActivityType.GuildRaidContribution).Counter.Should().Be(1_000_000);
    }

    [Fact]
    public async Task EventLedger_RecordThenHas_GuardsExactlyOnce()
    {
        var pid = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new PlayerMasteryActivityRepository(db);

        (await repo.HasEventAsync(pid, MasteryActivityType.RaidKill, "k1")).Should().BeFalse();
        await repo.RecordEventAsync(pid, MasteryActivityType.RaidKill, "k1");
        (await repo.HasEventAsync(pid, MasteryActivityType.RaidKill, "k1")).Should().BeTrue();
        // A different reference / type is independent.
        (await repo.HasEventAsync(pid, MasteryActivityType.RaidKill, "k2")).Should().BeFalse();
        (await repo.HasEventAsync(pid, MasteryActivityType.GauntletRankEarned, "k1")).Should().BeFalse();
    }
}
