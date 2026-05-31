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
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// HOW TO VERIFY THIS TEST REQUIRES THE LOCK:
// In AtomicWithAdvisoryLockAsync, replace the pg_advisory_xact_lock call with a no-op
// (e.g. comment it out). Run this test repeatedly — without the advisory lock, concurrent
// sessions can both read count=0, both pass the slot-cap check, and both insert, violating
// the 1-slot cap. The DB count assertion then finds 2 rows instead of 1.
//
// Note: both available raid definitions are Tier=World. The integration test uses
// isAdmin:true to bypass the world gate and exercise the slot-cap advisory lock directly.
// World-gate unit tests cover the non-admin denial path.
public class MagicSlotConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_magic_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        _redis = new RedisBuilder().Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

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
                        ["Jwt:Issuer"]                          = "rota-magic-test",
                        ["Jwt:Audience"]                        = "rota-magic-test",
                        ["Admin:PlayerIds:0"]                   = Guid.Empty.ToString(),
                        ["Seed:AdminPassword"]                  = "",
                        // Personal raids = 1 magic slot (verifies config binding)
                        ["MagicConfig:SlotsBySize:Personal"]    = "1",
                    });
                });
            });

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
    // Slot-cap race test — the core advisory-lock verification
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentApplyMagic_SlotCap_AdvisoryLockPreventsOverAllocation()
    {
        // ---- Seed -------------------------------------------------------
        // All available raids are Tier=World, so we use isAdmin:true to bypass
        // the world gate and focus exclusively on the slot-cap advisory-lock race.
        Player summoner;
        ActiveRaid raid;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

            summoner = Player.Create("magic_sum", "magic_sum@test.dev", "hash");
            db.Players.Add(summoner);

            // Personal raid = 1 magic slot (from config above)
            raid = ActiveRaid.Create(
                "raid_ironcolossus",
                summoner.Id,
                maxHp: 1_000_000L,
                expiresAt: DateTimeOffset.UtcNow.AddHours(48),
                difficulty: RaidDifficulty.Normal,
                size: RaidSize.Personal);
            db.ActiveRaids.Add(raid);

            // Summoner owns two distinct magics to avoid the DuplicateMagic short-circuit.
            // Two different defIds → both pass duplicate check; only the slot-cap race matters.
            db.PlayerMagics.Add(PlayerMagic.Create(summoner.Id, "magic_whetstone"));
            db.PlayerMagics.Add(PlayerMagic.Create(summoner.Id, "magic_poison"));

            await db.SaveChangesAsync();
        }

        // ---- Concurrent apply -------------------------------------------
        // Two separate DI scopes = two RotaDbContext instances = two PostgreSQL connections.
        // Task.Run forces genuine thread-pool parallelism so both race for the slot at once.
        //
        // Both apply different magic IDs, so DuplicateMagic does not fire inside the lock.
        // The ONLY gate that can serialise them is the count→slot-cap advisory lock.
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        var svc1 = scope1.ServiceProvider.GetRequiredService<IMagicService>();
        var svc2 = scope2.ServiceProvider.GetRequiredService<IMagicService>();

        // isAdmin:true — bypasses world gate (unit tests cover non-admin denial).
        // Both calls race to occupy the single magic slot on this Personal raid.
        var results = await Task.WhenAll(
            Task.Run(async () => await svc1.ApplyMagicAsync(
                summoner.Id, raid.Id, "magic_whetstone", isAdmin: true)),
            Task.Run(async () => await svc2.ApplyMagicAsync(
                summoner.Id, raid.Id, "magic_poison", isAdmin: true)));

        // ---- Assertions -------------------------------------------------

        // 1. Exactly one apply succeeded; the other was blocked by the slot cap
        //    or by the one-per-player pre-check (which fires before the advisory lock).
        //    In either case, the slot-cap advisory lock is what prevents double-insert
        //    in the race where both pass the pre-check and enter the lock concurrently.
        var successCount = results.Count(r => r.Success);
        successCount.Should().Be(1,
            "advisory lock must allow exactly one apply when only one slot is available");

        // 2. Exactly one RaidMagic row — the advisory lock must prevent over-allocation.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var count = await db.RaidMagics
                .CountAsync(m => m.ActiveRaidId == raid.Id && !m.IsDeleted);
            count.Should().Be(1,
                "advisory lock must ensure at most one magic is inserted on a 1-slot raid");
        }
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

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
        return AppContext.BaseDirectory;
    }
}
