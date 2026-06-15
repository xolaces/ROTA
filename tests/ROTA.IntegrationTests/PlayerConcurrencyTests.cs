using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// T59 — players-row optimistic concurrency. The audit found simultaneous quest+raid reward writes
// were last-write-wins (silent XP/gold loss): both requests loaded the player, both saved full
// columns, the later commit erased the earlier one. The fix maps Postgres `xmin` as the concurrency
// token and routes reward writes through PlayerRepository.MutateWithRetryAsync (reload + re-apply
// on conflict).
//
// HOW TO VERIFY THESE TESTS REQUIRE THE FIX:
// (a) Remove the xmin mapping from PlayerConfiguration — StaleFullColumnSave_Throws fails (the
//     stale save silently wins) and ConcurrentMutations can lose updates.
// (b) Replace MutateWithRetryAsync's catch/reload with a rethrow — ConcurrentMutations fails with
//     DbUpdateConcurrencyException instead of converging.
public class PlayerConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_player_conc_test")
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
        var player = Player.Create($"conc_{suffix}", $"{suffix}@test.dev", "hash");
        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    // The token itself: a stale full-column save must throw, not silently win.
    [Fact]
    public async Task StaleFullColumnSave_Throws_InsteadOfLastWriteWins()
    {
        var pid = await SeedPlayerAsync();

        // Two contexts load the same row (same xmin snapshot).
        await using var dbA = NewDbContext();
        await using var dbB = NewDbContext();
        var playerA = await dbA.Players.SingleAsync(p => p.Id == pid);
        var playerB = await dbB.Players.SingleAsync(p => p.Id == pid);

        // A commits first — bumps xmin.
        playerA.AddGold(100);
        await dbA.SaveChangesAsync();

        // B's save carries the stale xmin: before T59 this silently erased A's +100.
        playerB.AddGold(50);
        var act = async () => await dbB.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "the xmin token must reject a save based on a stale read of the players row");
    }

    // The retry chokepoint: simultaneous reward writes converge with no loss.
    [Fact]
    public async Task ConcurrentMutations_GoldAndXp_NothingLost()
    {
        var pid = await SeedPlayerAsync();

        // 8 concurrent "reward grants" — each in its own context/repository, like simultaneous
        // quest-complete and raid-hit requests. Each adds 10 gold + 7 XP.
        const int writers = 8;
        var tasks = Enumerable.Range(0, writers).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDbContext();
            var repo = new PlayerRepository(db);
            return await repo.MutateWithRetryAsync(pid, p =>
            {
                p.AddGold(10);
                return p.AddExperience(7, _ => 1_000_000); // huge threshold — no level-ups in play
            });
        }));

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.Should().BeEmpty("no writer should cross the level threshold"));

        await using (var check = NewDbContext())
        {
            var final = await check.Players.SingleAsync(p => p.Id == pid);
            final.Gold.Should().Be(10L * writers, "every concurrent gold grant must survive");
            final.Experience.Should().Be(7L * writers, "every concurrent XP grant must survive");
        }
    }

    [Fact]
    public async Task MutateWithRetry_MissingPlayer_Throws()
    {
        await using var db = NewDbContext();
        var repo = new PlayerRepository(db);
        var act = async () => await repo.MutateWithRetryAsync(Guid.NewGuid(), p => { p.AddGold(1); return 0; });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
