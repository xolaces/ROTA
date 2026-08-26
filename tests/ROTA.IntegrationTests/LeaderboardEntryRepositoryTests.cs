using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// HOW TO VERIFY THE CONCURRENCY TEST ACTUALLY GUARDS AGAINST LOST UPDATES:
// In LeaderboardEntryRepository.IncrementAsync, replace the ON CONFLICT DO UPDATE with a
// SELECT then UPDATE approach (read value, add, write back). Run the concurrency test — it
// will fail because parallel reads see the same value before any write commits.  The ON CONFLICT
// DO UPDATE approach serialises the read-modify-write inside PostgreSQL and the test passes.
public class LeaderboardEntryRepositoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_lb_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgres.StartAsync();

        // Apply all EF migrations (includes AddLeaderboardEntry) so the schema is correct.
        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private RotaDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new RotaDbContext(options);
    }

    private async Task<ILeaderboardEntryRepository> NewRepoAsync()
    {
        var db = NewDbContext();
        await db.Database.OpenConnectionAsync();
        return new LeaderboardEntryRepository(db);
    }

    // Seeds a Player row (required by the FK on leaderboard_entry) and returns the player's Id.
    // Username is capped to 32 chars (DB constraint). We use an 8-char hex suffix so the
    // generated name is "lb_{hex8}" = 11 chars — well within the 32-char limit.
    private async Task<Guid> SeedPlayerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"lb_{suffix}", $"{suffix}@test.dev", "hash");
        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    // ── Test 1: first increment creates; second accumulates ──────────────────

    [Fact]
    public async Task Increment_CreatesOnFirstCall_ThenAccumulatesOnSecond()
    {
        var playerId  = await SeedPlayerAsync();
        const string periodKey = "week:2026-W23";
        var at        = DateTimeOffset.UtcNow;
        await using var db = NewDbContext();
        var repo = new LeaderboardEntryRepository(db);

        await repo.IncrementAsync(playerId, LeaderboardBoard.DamageDealt,
            LeaderboardPeriod.Weekly, periodKey, 500L, at);

        var entry1 = await db.LeaderboardEntries
            .AsNoTracking()
            .SingleAsync(e => e.PlayerId == playerId && e.Board == LeaderboardBoard.DamageDealt);
        entry1.Value.Should().Be(500L, "first call should create the row with the given delta");

        await repo.IncrementAsync(playerId, LeaderboardBoard.DamageDealt,
            LeaderboardPeriod.Weekly, periodKey, 300L, at.AddSeconds(1));

        var entry2 = await db.LeaderboardEntries
            .AsNoTracking()
            .SingleAsync(e => e.PlayerId == playerId && e.Board == LeaderboardBoard.DamageDealt);
        entry2.Value.Should().Be(800L, "second call should add 300 to the existing 500");
    }

    // ── Test 2: CONCURRENCY — N parallel increments sum exactly ──────────────

    [Fact]
    public async Task Increment_Concurrent_NoLostUpdates()
    {
        // This is the core correctness test for Slice 2.
        // 20 parallel tasks each increment by 100.  The ON CONFLICT DO UPDATE serialises
        // the read-modify-write at the PostgreSQL row level, so every delta lands exactly.
        // If a read-then-write pattern were used instead, some tasks would see the same old
        // value and overwrite each other's increments — we would end up with less than 2000.

        const int taskCount = 20;
        const long delta    = 100L;
        const long expected = taskCount * delta; // 2000

        var playerId  = await SeedPlayerAsync();
        const string periodKey = "week:2026-W23";
        var at        = DateTimeOffset.UtcNow;

        // Each task gets its own DbContext (= its own Npgsql connection) so the DB engine
        // receives genuinely concurrent requests.
        var tasks = Enumerable.Range(0, taskCount).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDbContext();
            var repo = new LeaderboardEntryRepository(db);
            await repo.IncrementAsync(playerId, LeaderboardBoard.DamageDealt,
                LeaderboardPeriod.Weekly, periodKey, delta, at);
        })).ToArray();

        await Task.WhenAll(tasks);

        await using var readDb = NewDbContext();
        var entry = await readDb.LeaderboardEntries
            .AsNoTracking()
            .SingleAsync(e => e.PlayerId == playerId
                           && e.Board == LeaderboardBoard.DamageDealt
                           && e.PeriodKey == periodKey);

        entry.Value.Should().Be(expected,
            $"all {taskCount} concurrent increments of {delta} must sum to {expected} with no lost updates");
    }

    // ── Test 3: MaxUpdate only raises; smaller candidate does not lower value ─

    [Fact]
    public async Task MaxUpdate_OnlyRaises_SmallerCandidateIsIgnored()
    {
        var playerId  = await SeedPlayerAsync();
        const string periodKey = "day:2026-06-03";
        var t0 = new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(1);
        var t2 = t0.AddMinutes(2);

        await using var db = NewDbContext();
        var repo = new LeaderboardEntryRepository(db);

        // First call — inserts with value 1000.
        await repo.MaxUpdateAsync(playerId, LeaderboardBoard.LargestHit,
            LeaderboardPeriod.Daily, periodKey, 1000L, t0);

        // Second call — larger candidate; value should rise to 2500.
        await repo.MaxUpdateAsync(playerId, LeaderboardBoard.LargestHit,
            LeaderboardPeriod.Daily, periodKey, 2500L, t1);

        // Third call — smaller candidate; value must stay at 2500 and LastProgressAt
        // must remain t1 (the "earliest to reach" tiebreak must not regress).
        await repo.MaxUpdateAsync(playerId, LeaderboardBoard.LargestHit,
            LeaderboardPeriod.Daily, periodKey, 800L, t2);

        var entry = await db.LeaderboardEntries
            .AsNoTracking()
            .SingleAsync(e => e.PlayerId == playerId && e.Board == LeaderboardBoard.LargestHit);

        entry.Value.Should().Be(2500L, "value must not be lowered by a smaller candidate");
        entry.LastProgressAt.Should().Be(t1,
            "last_progress_at must not move when the candidate did not raise the value");
    }

    [Fact]
    public async Task MaxUpdate_LargerCandidateAfterSmaller_RaisesValueAndTimestamp()
    {
        var playerId  = await SeedPlayerAsync();
        const string periodKey = "day:2026-06-03";
        var t0 = new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(5);

        await using var db = NewDbContext();
        var repo = new LeaderboardEntryRepository(db);

        await repo.MaxUpdateAsync(playerId, LeaderboardBoard.LargestHit,
            LeaderboardPeriod.Daily, periodKey, 100L, t0);

        await repo.MaxUpdateAsync(playerId, LeaderboardBoard.LargestHit,
            LeaderboardPeriod.Daily, periodKey, 9999L, t1);

        var entry = await db.LeaderboardEntries
            .AsNoTracking()
            .SingleAsync(e => e.PlayerId == playerId && e.Board == LeaderboardBoard.LargestHit);

        entry.Value.Should().Be(9999L);
        entry.LastProgressAt.Should().Be(t1);
    }

    // ── Test 4: page read ordered value DESC, last_progress_at ASC (tiebreak) ─

    [Fact]
    public async Task GetPageAsync_OrderedByValueDescThenLastProgressAtAsc()
    {
        // Seed four players.  P1 and P2 tie on value but P1 reached it first.
        // Expected ranking: P3 (highest), P1 (tie-winner — earlier), P2 (tie-loser — later), P4 (lowest).

        var p1 = await SeedPlayerAsync();
        var p2 = await SeedPlayerAsync();
        var p3 = await SeedPlayerAsync();
        var p4 = await SeedPlayerAsync();

        const string periodKey = "week:2026-W23";
        var t = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero);

        await using var db = NewDbContext();
        var repo = new LeaderboardEntryRepository(db);

        await repo.IncrementAsync(p3, LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, periodKey, 5000L, t);
        await repo.IncrementAsync(p1, LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, periodKey, 3000L, t.AddMinutes(1));
        await repo.IncrementAsync(p2, LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, periodKey, 3000L, t.AddMinutes(2));  // same value, later timestamp
        await repo.IncrementAsync(p4, LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, periodKey, 1000L, t);

        var page = await repo.GetPageAsync(LeaderboardBoard.DamageDealt, periodKey, page: 1, pageSize: 200);

        // The four entries should be ordered: P3, P1, P2, P4.
        page.Count.Should().BeGreaterThanOrEqualTo(4);
        var relevant = page.Where(e => new[] { p1, p2, p3, p4 }.Contains(e.PlayerId)).ToList();
        relevant.Should().HaveCount(4);
        relevant[0].PlayerId.Should().Be(p3, "P3 has the highest value");
        relevant[1].PlayerId.Should().Be(p1, "P1 and P2 tie; P1 reached it first (earlier LastProgressAt)");
        relevant[2].PlayerId.Should().Be(p2, "P2 reached the tied value later");
        relevant[3].PlayerId.Should().Be(p4, "P4 has the lowest value");
    }

    // ── Test 5: GetPlayerEntryAsync returns row / null ────────────────────────

    [Fact]
    public async Task GetPlayerEntryAsync_ReturnsEntry_WhenExists()
    {
        var playerId  = await SeedPlayerAsync();
        const string periodKey = "month:2026-06";
        var at        = DateTimeOffset.UtcNow;

        await using var db = NewDbContext();
        var repo = new LeaderboardEntryRepository(db);

        await repo.IncrementAsync(playerId, LeaderboardBoard.EnergySpent,
            LeaderboardPeriod.Monthly, periodKey, 150L, at);

        var entry = await repo.GetPlayerEntryAsync(playerId, LeaderboardBoard.EnergySpent, periodKey);

        entry.Should().NotBeNull();
        entry!.PlayerId.Should().Be(playerId);
        entry.Value.Should().Be(150L);
    }

    [Fact]
    public async Task GetPlayerEntryAsync_ReturnsNull_WhenAbsent()
    {
        var playerId = Guid.NewGuid(); // No row seeded for this player.
        await using var db = NewDbContext();
        var repo = new LeaderboardEntryRepository(db);

        var entry = await repo.GetPlayerEntryAsync(playerId, LeaderboardBoard.EnergySpent, "month:2026-06");

        entry.Should().BeNull();
    }

    // ── Test 6: soft-deleted rows are excluded from page reads ────────────────

    [Fact]
    public async Task GetPageAsync_ExcludesSoftDeletedRows()
    {
        var alivePlayer   = await SeedPlayerAsync();
        var deletedPlayer = await SeedPlayerAsync();
        const string periodKey = "week:2026-W23";
        var at = DateTimeOffset.UtcNow;

        await using var db = NewDbContext();
        var repo = new LeaderboardEntryRepository(db);

        await repo.IncrementAsync(alivePlayer,   LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, periodKey, 9000L, at);
        await repo.IncrementAsync(deletedPlayer, LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly, periodKey, 8000L, at);

        // Soft-delete the deletedPlayer's row directly via EF so we can test the filter.
        var rowToDelete = await db.LeaderboardEntries
            .FirstAsync(e => e.PlayerId == deletedPlayer
                          && e.Board == LeaderboardBoard.DamageDealt
                          && e.PeriodKey == periodKey);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE leaderboard_entry SET is_deleted = true WHERE id = {0}",
            parameters: [rowToDelete.Id]);

        var page = await repo.GetPageAsync(LeaderboardBoard.DamageDealt, periodKey, 1, 200);

        page.Should().NotContain(e => e.PlayerId == deletedPlayer,
            "soft-deleted entries must be excluded from page reads");
        page.Should().Contain(e => e.PlayerId == alivePlayer);
    }
}
