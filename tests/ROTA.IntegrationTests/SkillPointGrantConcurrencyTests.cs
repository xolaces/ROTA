using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// Skill-point grants are an ATOMIC database increment, not a read-modify-write.
//
// THE DEFECT THESE PIN. PlayerStats carries no concurrency token — the xmin token is on `players`
// only — and the grant callers do not share a lock domain. A raid loot claim serializes on the
// PARTICIPANT row (RaidService.LootRaidAsync takes the advisory lock on participant.Id), while a quest
// attempt or item use serializes on the PLAYER. So a player claiming two lootable raids at once ran
// both grants concurrently: both read the same starting total, both wrote start+N, and one grant
// vanished. It was unrecoverable, because the claim latch had already stamped rewarded_at on BOTH
// participant rows — pressing Loot again returns the summary and grants nothing.
//
// Doing the arithmetic in the database removes the read entirely, so no lock discipline is required of
// the callers at all.
public class SkillPointGrantConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_skillpoint_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
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
        var player = Player.Create($"sp_{suffix}", $"{suffix}@test.dev", "hash");
        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    private async Task<long> SkillPointsAsync(Guid playerId)
    {
        await using var db = NewDbContext();
        var stats = await db.PlayerStats.AsNoTracking().FirstAsync(s => s.PlayerId == playerId);
        return stats.SkillPoints;
    }

    [Fact]
    public async Task ConcurrentGrants_AreAllKept()
    {
        const int grants = 25;
        const long each  = 10;

        var playerId = await SeedPlayerAsync();

        // Each grant gets its OWN DbContext, and therefore its own connection — otherwise they would
        // serialize on a single connection and the race this exists to catch could never occur.
        var tasks = Enumerable.Range(0, grants).Select(async _ =>
        {
            await using var db = NewDbContext();
            await new PlayerRepository(db).IncrementSkillPointsAsync(playerId, each);
        });

        await Task.WhenAll(tasks);

        (await SkillPointsAsync(playerId)).Should().Be(grants * each,
            "every concurrent grant must land — a read-modify-write kept only the last writer's total");
    }

    [Fact]
    public async Task AGrant_ReturnsTheCommittedTotal_NotTheAmountAdded()
    {
        var playerId = await SeedPlayerAsync();

        await using var db = NewDbContext();
        var repo = new PlayerRepository(db);

        (await repo.IncrementSkillPointsAsync(playerId, 7)).Should().Be(7);
        (await repo.IncrementSkillPointsAsync(playerId, 5)).Should().Be(12,
            "RETURNING hands back the committed running total, so no caller needs a second read");
    }

    [Fact]
    public async Task AGrant_ForAPlayerWithNoStatsRow_GrantsNothing_AndDoesNotThrow()
    {
        await using var db = NewDbContext();
        var repo = new PlayerRepository(db);

        (await repo.IncrementSkillPointsAsync(Guid.NewGuid(), 10)).Should().Be(0);
    }

    [Fact]
    public async Task WritingTheStatsRow_DoesNotClobberAConcurrentGrant()
    {
        // UpdateStatsAsync used to force EVERY column Modified, so a full-row save also rewrote
        // skill_points from a possibly-stale in-memory value — silently undoing a grant that had
        // committed in between. A tracked entity now saves only what actually changed.
        var playerId = await SeedPlayerAsync();

        await using var reader = NewDbContext();
        var stats = await reader.PlayerStats.FirstAsync(s => s.PlayerId == playerId);

        // A grant commits on another connection while `stats` is held with skill_points == 0.
        await using (var other = NewDbContext())
            await new PlayerRepository(other).IncrementSkillPointsAsync(playerId, 50);

        // Now persist an unrelated change to the stale entity.
        stats.RestoreFullHealth();
        await new PlayerRepository(reader).UpdateStatsAsync(stats);

        (await SkillPointsAsync(playerId)).Should().Be(50,
            "saving an unrelated field must not carry a stale skill_points value over a live grant");
    }
}
