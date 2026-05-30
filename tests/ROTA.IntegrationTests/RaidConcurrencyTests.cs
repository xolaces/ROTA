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
using ROTA.Shared.DTOs;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// HOW TO VERIFY THIS TEST REQUIRES THE LOCK:
// In ActiveRaidRepository.AtomicApplyHitAsync, replace the FOR UPDATE query with a plain
// _db.ActiveRaids.FirstOrDefaultAsync() and remove the transaction.  Run this test — it will
// fail intermittently (or consistently under load) with double gem grants, an un-defeated raid,
// or incorrect damage totals.  Restore AtomicApplyHitAsync to make it green.
//
// This was manually verified during development: reverting to plain UpdateAsync caused the test
// to fail with gemRows == 2 (both players received gem rewards).
public class RaidConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        _redis = new RedisBuilder().Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Ephemeral RSA key — only the public key is given to the app.
        // We call services directly (not via HTTP), so JWT signing is not needed here.
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseContentRoot(FindApiContentRoot());
                host.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["ConnectionStrings:Redis"]             = _redis.GetConnectionString(),
                        ["Jwt:PublicKey"]                       = publicKeyPem,
                        ["Jwt:Issuer"]                          = "rota-test",
                        ["Jwt:Audience"]                        = "rota-test",
                        // Seed one admin so the AdminOnly policy is parseable (not used in this test).
                        ["Admin:PlayerIds:0"]                   = Guid.Empty.ToString(),
                        // Neutralize the startup admin seeder: keeps this fixture hermetic against a
                        // developer's Seed:AdminPassword user-secret. With it set, the seeder would
                        // query players during host startup — before this fixture applies migrations.
                        ["Seed:AdminPassword"]                  = "",
                    });
                });
            });

        // Apply every EF Core migration against the Testcontainers Postgres instance.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    // -----------------------------------------------------------------------
    // Concurrency test
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentKillingHits_ExactlyOneKill_LoserStaminaRefunded()
    {
        // ---- Seed -------------------------------------------------------
        Player player1, player2;
        ActiveRaid raid;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

            // Player.Create seeds PlayerStats and three PlayerResource rows (Energy/Stamina/GuildStamina).
            player1 = Player.Create("p1", "p1@test.dev", "test-hash");
            player2 = Player.Create("p2", "p2@test.dev", "test-hash");
            db.Players.AddRange(player1, player2);

            // HP = 1 → any hit (minimum damage = 1) kills the raid.
            // raid_ironcolossus StaminaCostPerHit = 1; hitSize = 1 → cost = 1 stamina.
            raid = ActiveRaid.Create(
                "raid_ironcolossus",
                player1.Id,
                maxHp: 1L,
                expiresAt: DateTimeOffset.UtcNow.AddHours(48),
                difficulty: RaidDifficulty.Normal);
            db.ActiveRaids.Add(raid);

            await db.SaveChangesAsync();
        }

        // Read stamina checkpoints before hitting.
        int p1Before, p2Before;
        using (var scope = _factory.Services.CreateScope())
        {
            var energy = scope.ServiceProvider.GetRequiredService<IEnergyService>();
            p1Before = await energy.GetCurrentEnergyAsync(player1.Id, ResourceType.Stamina, default);
            p2Before = await energy.GetCurrentEnergyAsync(player2.Id, ResourceType.Stamina, default);
        }

        p1Before.Should().BeGreaterThan(0, "player1 must have stamina to hit");
        p2Before.Should().BeGreaterThan(0, "player2 must have stamina to hit");

        // ---- Concurrent hits -------------------------------------------
        // Two separate DI scopes = two RotaDbContext instances = two PostgreSQL connections.
        // This is the critical requirement for FOR UPDATE to serialise both requests.
        // Task.Run forces genuine thread-pool parallelism so both race for the lock at once.
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        var raidService1 = scope1.ServiceProvider.GetRequiredService<IRaidService>();
        var raidService2 = scope2.ServiceProvider.GetRequiredService<IRaidService>();

        // Unique keys per run — prevents stale local-Redis idempotency entries
        // from a prior test run from short-circuiting the race entirely.
        var results = await Task.WhenAll(
            Task.Run(async () => await raidService1.HitRaidAsync(player1.Id, raid.Id, 1, Guid.NewGuid().ToString())),
            Task.Run(async () => await raidService2.HitRaidAsync(player2.Id, raid.Id, 1, Guid.NewGuid().ToString())));

        // ---- Assertions ------------------------------------------------

        // 1. Exactly one request killed the raid; the other hit the race condition.
        var successes   = results.Count(r => r.Success);
        var raceDefeats = results.Count(r => !r.Success
                            && r.FailureCode == RaidHitFailureCode.RaidAlreadyDefeated);

        successes.Should().Be(1,
            "exactly one request should land the killing blow");
        raceDefeats.Should().Be(1,
            "the other request should lose the race and receive a refund");

        // 2. Raid is marked defeated exactly once in PostgreSQL.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var dbRaid = await db.ActiveRaids.AsNoTracking().FirstAsync(r => r.Id == raid.Id);
            dbRaid.IsDefeated.Should().BeTrue("raid must be defeated in the DB");
            dbRaid.CurrentHp.Should().Be(0, "HP must be zero after kill");
        }

        // 3. Gem rewards granted exactly once across both players.
        //    IronColossus BaseGemReward = 2; in a 1v1 the single player is Legendary1
        //    and receives gems.  The race-loser never dealt damage so gets no gems.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var gemRows = await db.GemTransactions
                .Where(g => g.ReferenceId == $"raid:{raid.Id}:{player1.Id}"
                         || g.ReferenceId == $"raid:{raid.Id}:{player2.Id}")
                .CountAsync();
            gemRows.Should().Be(1,
                "gems must be granted exactly once — duplicate rewards are the bug this lock prevents");
        }

        // 4. The losing player's stamina is net-unchanged (spent then refunded).
        //    The winning player's stamina is reduced by 1 (spent, not refunded).
        int p1After, p2After;
        using (var scope = _factory.Services.CreateScope())
        {
            var energy = scope.ServiceProvider.GetRequiredService<IEnergyService>();
            p1After = await energy.GetCurrentEnergyAsync(player1.Id, ResourceType.Stamina, default);
            p2After = await energy.GetCurrentEnergyAsync(player2.Id, ResourceType.Stamina, default);
        }

        var staminaDeltas = new[]
        {
            p1Before - p1After,
            p2Before - p2After,
        };

        staminaDeltas.Should().Contain(1,
            "the winner spent 1 stamina and was not refunded");
        staminaDeltas.Should().Contain(0,
            "the loser spent 1 stamina then received a full refund — net zero");
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    // Walk up from the test binary directory until we find src/ROTA.Api with a content/ folder.
    // Works for both local `dotnet test` runs and CI pipelines that clone the full repo.
    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ROTA.Api");
            if (Directory.Exists(Path.Combine(candidate, "content")))
                return candidate;
            dir = dir.Parent;
        }
        // Fallback — if content files were copied to the test output directory.
        return AppContext.BaseDirectory;
    }
}
