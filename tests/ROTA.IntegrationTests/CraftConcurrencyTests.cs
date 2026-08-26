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
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// System 26 slice 2 (D-018) — crafting's whole safety story is that verify, charge, consume and grant
// run in ONE transaction under the per-player advisory lock. Nothing about that can be proven with
// mocks, because the pass-through test lock has neither a transaction nor a lock.
//
// HOW TO VERIFY THESE TESTS CATCH THE BUG: take CraftAsync off IPlayerMutationLock (call
// CraftCoreAsync directly). ConcurrentCrafts_ProduceExactlyOneOutput then fails with two winners —
// both readers see the ingredients before either consumes them, and the player crafts a second
// tier-II unit out of one set of materials.
public class CraftConcurrencyTests : IAsyncLifetime
{
    // The shipped General recipe: Ironward + 2 oathsteel + 10 iron shard + 15,000 gold → Ironward II.
    private const string RecipeId  = "craft_ironward_ii";
    private const string InUnit    = "gen_ironward";
    private const string OutUnit   = "gen_ironward_ii";
    private const string MatA      = "mat_oathsteel";
    private const string MatB      = "mat_iron_shard";
    private const long   GoldCost  = 15_000;

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_crafting_test")
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

    /// <summary>Seeds a player holding EXACTLY one craft's worth of ingredients.</summary>
    private async Task<Guid> SeedCrafterAsync(long gold, int oathsteel = 2, int ironShard = 10)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var player = Player.Create($"craft_{Guid.NewGuid():N}"[..20], $"{Guid.NewGuid():N}@test.io", "hash");
        player.AddGold(gold);
        db.Players.Add(player);

        db.PlayerUnits.Add(PlayerUnit.Create(player.Id, InUnit));
        db.PlayerInventoryItems.Add(PlayerInventoryItem.Create(player.Id, MatA, oathsteel));
        db.PlayerInventoryItems.Add(PlayerInventoryItem.Create(player.Id, MatB, ironShard));

        await db.SaveChangesAsync();
        return player.Id;
    }

    private sealed record Snapshot(
        long Gold, bool OwnsInput, bool OwnsOutput, int Oathsteel, int IronShard, int CraftAudits);

    private async Task<Snapshot> ReadAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var gold = (await db.Players.AsNoTracking().FirstAsync(p => p.Id == playerId)).Gold;

        var units = await db.PlayerUnits.AsNoTracking()
            .Where(u => u.PlayerId == playerId && !u.IsDeleted)
            .Select(u => u.UnitDefinitionId).ToListAsync();

        var inv = await db.PlayerInventoryItems.AsNoTracking()
            .Where(i => i.PlayerId == playerId)
            .ToDictionaryAsync(i => i.ItemDefinitionId, i => i.Quantity);

        var audits = await db.AuditLogs.AsNoTracking()
            .CountAsync(a => a.PlayerId == playerId && a.Action == "ItemCrafted");

        return new Snapshot(gold, units.Contains(InUnit), units.Contains(OutUnit),
            inv.GetValueOrDefault(MatA), inv.GetValueOrDefault(MatB), audits);
    }

    [Fact]
    public async Task Craft_TakesExactlyWhatTheRecipeNames_AndGrantsTheOutput()
    {
        var playerId = await SeedCrafterAsync(gold: 100_000, oathsteel: 5, ironShard: 20);

        using var scope = _factory.Services.CreateScope();
        var crafting = scope.ServiceProvider.GetRequiredService<ICraftingService>();

        var result = await crafting.CraftAsync(playerId, RecipeId);

        result.Success.Should().BeTrue(result.FailureReason);
        result.GoldSpent.Should().Be(GoldCost);

        var after = await ReadAsync(playerId);
        after.Gold.Should().Be(100_000 - GoldCost);
        after.OwnsInput.Should().BeFalse("the consumed general is dissolved into its upgrade");
        after.OwnsOutput.Should().BeTrue();
        after.Oathsteel.Should().Be(3, "2 of 5 are named by the recipe");
        after.IronShard.Should().Be(10, "10 of 20 are named by the recipe");
        after.CraftAudits.Should().Be(1);
    }

    // The property slice 2 exists to guarantee. One set of ingredients, eight simultaneous crafts:
    // exactly one may win, and the losers must cost the player nothing.
    [Fact]
    public async Task ConcurrentCrafts_ProduceExactlyOneOutput()
    {
        const int attempts = 8;
        // Deliberately enough gold for many crafts, so gold is never what limits the winners —
        // the ingredient check under the lock has to be what stops them.
        var playerId = await SeedCrafterAsync(gold: 500_000);

        var tasks = Enumerable.Range(0, attempts).Select(async _ =>
        {
            // A scope each, so every attempt gets its own DbContext and connection — a shared context
            // would serialise them and the race would never happen.
            using var scope = _factory.Services.CreateScope();
            var crafting = scope.ServiceProvider.GetRequiredService<ICraftingService>();
            return await crafting.CraftAsync(playerId, RecipeId);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Success).Should().Be(1,
            "one set of ingredients may only ever produce one output");

        // Every loser must give a reason a player can act on, never a crash surfacing as a 500.
        foreach (var loss in results.Where(r => !r.Success))
            loss.FailureCode.Should().BeOneOf(
                CraftFailureCode.AlreadyOwned, CraftFailureCode.MissingIngredients);

        var after = await ReadAsync(playerId);
        after.Gold.Should().Be(500_000 - GoldCost, "gold may only be charged for the craft that won");
        after.OwnsOutput.Should().BeTrue();
        after.OwnsInput.Should().BeFalse();
        after.Oathsteel.Should().Be(0);
        after.IronShard.Should().Be(0);
        after.CraftAudits.Should().Be(1, "a losing craft must not leave an audit row behind");
    }

    [Fact]
    public async Task Craft_WhenGoldIsShort_ConsumesNothing()
    {
        var playerId = await SeedCrafterAsync(gold: GoldCost - 1);

        using var scope = _factory.Services.CreateScope();
        var crafting = scope.ServiceProvider.GetRequiredService<ICraftingService>();

        var result = await crafting.CraftAsync(playerId, RecipeId);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CraftFailureCode.InsufficientGold);

        var after = await ReadAsync(playerId);
        after.Gold.Should().Be(GoldCost - 1);
        after.OwnsInput.Should().BeTrue("a refused craft must leave every ingredient where it was");
        after.OwnsOutput.Should().BeFalse();
        after.Oathsteel.Should().Be(2);
        after.IronShard.Should().Be(10);
        after.CraftAudits.Should().Be(0);
    }

    [Fact]
    public async Task Craft_WhenAMaterialIsShort_ChargesNoGold()
    {
        var playerId = await SeedCrafterAsync(gold: 100_000, oathsteel: 1);

        using var scope = _factory.Services.CreateScope();
        var crafting = scope.ServiceProvider.GetRequiredService<ICraftingService>();

        var result = await crafting.CraftAsync(playerId, RecipeId);

        result.FailureCode.Should().Be(CraftFailureCode.MissingIngredients);

        var after = await ReadAsync(playerId);
        after.Gold.Should().Be(100_000, "the affordability check must never run before the ingredient check");
        after.OwnsInput.Should().BeTrue();
        after.Oathsteel.Should().Be(1);
    }
}
