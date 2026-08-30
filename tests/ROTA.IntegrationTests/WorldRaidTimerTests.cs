using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// A World raid has no collective health: it is decided by a seven-day timer and pays on an absolute
// damage ladder (owner 2026-08-29). raids.json therefore carries baseHp 0 for both World raids, and
// RaidDefinitionProvider.Validate enforces that only World raids may.
//
// THE DEFECT THIS PINS. SummonRaidAsync computes finalHp = baseHp × difficultyMultiplier, which is 0
// for a World raid, so the raid is created with MaxHp = CurrentHp = 0. HitRaidAsync then evaluates
//
//     bool isKill = lockedRaid.CurrentHp == 0;
//
// which is ALREADY TRUE before any damage lands. The first hit therefore "killed" the raid instantly,
// flipped it to Lootable, and distributed kill rewards as though one player had soloed it — the timer
// never got the chance to run at all.
//
// The guard is that a raid with no health pool can never be killed BY DAMAGE. Settlement at expiry is
// a separate piece of work; these tests only prove the raid survives to reach it.
public class WorldRaidTimerTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_worldraid_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseContentRoot(FindApiContentRoot());
                host.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["ConnectionStrings:Redis"]             = _redis.GetConnectionString(),
                        ["Seed:AdminPassword"]                  = "",
                    }));
            });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RotaDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return Path.Combine(dir.FullName, "src", "ROTA.Api");
    }

    private static void LevelUpTo(Player player, int targetLevel, IStatService stats)
    {
        while (player.Level < targetLevel)
            player.AddExperience(stats.XpToNextLevel(player.Level), lvl => stats.XpToNextLevel(lvl));
    }

    /// <summary>Seeds a player and a raid with the given MaxHp, mirroring what SummonRaidAsync builds.</summary>
    private async Task<(Player player, ActiveRaid raid)> SeedAsync(string username, long maxHp)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var stats = scope.ServiceProvider.GetRequiredService<IStatService>();

        var player = Player.Create(username, $"{username}@rota.test", "hash");
        LevelUpTo(player, 100, stats);
        db.Players.Add(player);

        var raid = ActiveRaid.Create(
            "raid_ironcolossus", player.Id, maxHp,
            expiresAt: DateTimeOffset.UtcNow.AddHours(168),
            difficulty: RaidDifficulty.Normal);
        db.ActiveRaids.Add(raid);

        await db.SaveChangesAsync();
        return (player, raid);
    }

    private async Task<ActiveRaid> ReloadAsync(Guid raidId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        return await db.ActiveRaids.AsNoTracking().FirstAsync(r => r.Id == raidId);
    }

    // ── the defect ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AWorldRaid_SurvivesItsFirstHit_BecauseItHasNoHealthToLose()
    {
        var (player, raid) = await SeedAsync("world_first_hit", maxHp: 0);

        using var scope = _factory.Services.CreateScope();
        var raids = scope.ServiceProvider.GetRequiredService<IRaidService>();

        var result = await raids.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        result.Success.Should().BeTrue("a World raid is hittable for the whole seven days");

        var after = await ReloadAsync(raid.Id);

        after.IsDefeated.Should().BeFalse(
            "CurrentHp == 0 is the RESTING state of a raid with no health pool, not a kill — the "
            + "first hit used to end a seven-day event instantly");
        after.LifecycleState.Should().Be(RaidLifecycleState.Active);
    }

    [Fact]
    public async Task AWorldRaid_StaysHittableAcrossManyHits_AndKeepsBankingDamage()
    {
        var (player, raid) = await SeedAsync("world_many_hits", maxHp: 0);

        for (int i = 0; i < 5; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var raids = scope.ServiceProvider.GetRequiredService<IRaidService>();
            var r = await raids.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
            r.Success.Should().BeTrue($"hit {i + 1} must land — the ladder is a week-long climb");
        }

        (await ReloadAsync(raid.Id)).IsDefeated.Should().BeFalse();

        using var verify = _factory.Services.CreateScope();
        var participants = verify.ServiceProvider.GetRequiredService<IRaidParticipantRepository>();
        var me = await participants.FindByRaidAndPlayerAsync(raid.Id, player.Id);

        me.Should().NotBeNull();
        me!.TotalDamageDealt.Should().BeGreaterThan(0,
            "damage is what the ladder pays on, so it must accumulate even with no health pool");
        me.HitCount.Should().Be(5);
    }

    // ── the counterpart: ordinary raids must still die ────────────────────────

    [Fact]
    public async Task AnOrdinaryRaid_StillDiesWhenItsHealthReachesZero()
    {
        // The guard must key on "has no health pool", NOT on "current health is zero" — otherwise it
        // would make every raid in the game immortal.
        var (player, raid) = await SeedAsync("ordinary_dies", maxHp: 1);

        using var scope = _factory.Services.CreateScope();
        var raids = scope.ServiceProvider.GetRequiredService<IRaidService>();

        var result = await raids.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        result.Success.Should().BeTrue();

        var after = await ReloadAsync(raid.Id);
        after.CurrentHp.Should().Be(0);
        after.IsDefeated.Should().BeTrue("1 HP and any hit is a kill");
        after.LifecycleState.Should().Be(RaidLifecycleState.Lootable);
    }
}
