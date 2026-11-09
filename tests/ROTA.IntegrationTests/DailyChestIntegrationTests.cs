using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using ROTA.Shared.DTOs;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// System 29 S2 — the daily chest against the REAL DB, DI graph, advisory lock and SQL latches.
// Each parallel call gets its own DI scope (own DbContext, own connection), as concurrent HTTP
// requests would.
public class DailyChestIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_daily_chest_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using var rsa = RSA.Create(2048);
        var publicKeyPem  = rsa.ExportSubjectPublicKeyInfoPem();
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();

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
                        ["Jwt:PrivateKey"]                      = privateKeyPem,
                        ["Jwt:Issuer"]                          = "rota-test",
                        ["Jwt:Audience"]                        = "rota-test",
                        ["Admin:PlayerIds:0"]                   = Guid.Empty.ToString(),
                        ["Seed:AdminPassword"]                  = "",
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

    private async Task<Player> NewPlayerAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var player = Player.Create(name, $"{name}@rota.test", "hash");
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    private async Task<T> InScopeAsync<T>(Func<IDailyChestService, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<IDailyChestService>());
    }

    [Fact]
    public async Task ParallelFirstReads_StoreExactlyOneRoll()
    {
        var player = await NewPlayerAsync("chestreader");

        var reads = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => InScopeAsync(s => s.GetTodayAsync(player.Id))));

        reads.Should().OnlyContain(r => !r.Picked && r.Chests == null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var rows = await db.PlayerDailyChests.AsNoTracking().Where(c => c.PlayerId == player.Id).ToListAsync();
        rows.Should().ContainSingle();
        rows[0].GetContents().Should().HaveCount(PlayerDailyChest.ChestCount);
    }

    [Fact]
    public async Task ParallelPicks_GrantExactlyOnce()
    {
        var player = await NewPlayerAsync("chestpicker");
        await InScopeAsync(s => s.GetTodayAsync(player.Id));

        // Eight concurrent picks across all six chests: one wins, the rest see AlreadyPicked.
        var picks = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => InScopeAsync(s => s.PickAsync(player.Id, i % PlayerDailyChest.ChestCount))));

        var winners = picks.Where(p => p.Success).ToList();
        winners.Should().ContainSingle();
        picks.Where(p => !p.Success).Should().OnlyContain(p => p.FailureCode == DailyChestFailureCode.AlreadyPicked);

        var granted = winners[0].Granted!;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var inventory = await db.PlayerInventoryItems.AsNoTracking().Where(i => i.PlayerId == player.Id).ToListAsync();
        inventory.Should().ContainSingle(i => i.ItemDefinitionId == granted.ItemDefinitionId)
            .Which.Quantity.Should().Be(granted.Quantity, "the item is granted once, by the winning pick only");

        var chest = await db.PlayerDailyChests.AsNoTracking().SingleAsync(c => c.PlayerId == player.Id);
        chest.PickedIndex.Should().Be((short)winners[0].Chest!.PickedIndex!.Value);

        var audits = await db.AuditLogs.AsNoTracking()
            .Where(a => a.PlayerId == player.Id && a.Action == "DailyChestPicked").CountAsync();
        audits.Should().Be(1);
    }

    [Fact]
    public async Task ParallelPicksWithNoPriorRead_StillRollOnceAndGrantOnce()
    {
        var player = await NewPlayerAsync("chesteager");

        var picks = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(i => InScopeAsync(s => s.PickAsync(player.Id, i))));

        picks.Count(p => p.Success).Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        (await db.PlayerDailyChests.CountAsync(c => c.PlayerId == player.Id)).Should().Be(1);
        (await db.AuditLogs.CountAsync(a => a.PlayerId == player.Id && a.Action == "DailyChestRolled")).Should().Be(1);
    }

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
