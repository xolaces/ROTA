using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// D-008/D-013 — gold is the consumable shop's price rail, and gold is a COLUMN with no ledger, so it
// has none of the idempotency machinery the gem ledger gained. Its whole safety story is that
// IPlayerRepository.TrySpendGoldAsync re-checks affordability inside the same UPDATE that subtracts.
//
// HOW TO VERIFY THIS TEST CATCHES THE BUG: change TrySpendGoldAsync to a read-then-write —
// FindByIdAsync, compare Gold, AddGold(-amount), UpdateAsync. The concurrency test below then fails
// with a negative balance, which is exactly the shape that let concurrent gem buys overspend before
// the ledger got its advisory lock.
public class GoldSpendConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_goldspend_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder().Build();

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

    private async Task<Guid> SeedPlayerWithGoldAsync(long gold)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var player = Player.Create($"gold_{Guid.NewGuid():N}"[..20], $"{Guid.NewGuid():N}@test.io", "hash");
        player.AddGold(gold);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    private async Task<long> ReadGoldAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        return (await db.Players.AsNoTracking().FirstAsync(p => p.Id == playerId)).Gold;
    }

    [Fact]
    public async Task TrySpendGold_DebitsAndReturnsCommittedBalance()
    {
        var playerId = await SeedPlayerWithGoldAsync(10_000);

        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();

        var newGold = await players.TrySpendGoldAsync(playerId, 4_000);

        newGold.Should().Be(6_000);
        (await ReadGoldAsync(playerId)).Should().Be(6_000, "the debit must be committed, not just returned");
    }

    [Fact]
    public async Task TrySpendGold_WhenUnaffordable_WritesNothing()
    {
        var playerId = await SeedPlayerWithGoldAsync(1_000);

        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();

        var newGold = await players.TrySpendGoldAsync(playerId, 4_000);

        newGold.Should().BeNull("an unaffordable spend reports failure rather than throwing");
        (await ReadGoldAsync(playerId)).Should().Be(1_000, "a refused debit must leave the balance untouched");
    }

    // The property the whole design rests on: the balance can never go negative, however many callers
    // race. 10 simultaneous 4000-gold buys against a 10000 balance must settle at exactly 2 winners.
    [Fact]
    public async Task ConcurrentSpends_NeverDriveGoldNegative()
    {
        const long starting = 10_000;
        const long price    = 4_000;
        const int  attempts = 10;

        var playerId = await SeedPlayerWithGoldAsync(starting);

        var tasks = Enumerable.Range(0, attempts).Select(async _ =>
        {
            // A scope each, so every attempt gets its own DbContext and connection — a shared context
            // would serialise them and the race would never happen.
            using var scope = _factory.Services.CreateScope();
            var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            return await players.TrySpendGoldAsync(playerId, price);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        var winners = results.Count(r => r is not null);
        winners.Should().Be((int)(starting / price),
            "only as many spends as the balance covers may succeed");

        var finalGold = await ReadGoldAsync(playerId);
        finalGold.Should().Be(starting - winners * price);
        finalGold.Should().BeGreaterThanOrEqualTo(0, "gold must never go negative under contention");

        // Every winner's returned balance must be a real committed value, never a stale read.
        foreach (var r in results.Where(r => r is not null))
            r!.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task TrySpendGold_RejectsNonPositiveAmounts()
    {
        var playerId = await SeedPlayerWithGoldAsync(5_000);

        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();

        // A negative "spend" would ADD gold — the shop's own guards reject it earlier, but the
        // repository must not be a way around them.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => players.TrySpendGoldAsync(playerId, -1_000));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => players.TrySpendGoldAsync(playerId, 0));

        (await ReadGoldAsync(playerId)).Should().Be(5_000);
    }
}
