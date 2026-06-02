using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// HOW TO VERIFY THIS TEST CATCHES THE BUG:
// In LegionService.BuyUnitAsync, change the AlreadyProcessed branch to return
// BuyUnitFail(BuyFailureCode.InsufficientGems, …) instead of calling UpsertAsync.
// The test will fail: the unit is not re-granted on retry.
//
// This test exercises the exact lost-purchase scenario:
//   1. Player has gems and buys a unit → gem ledger row written + unit granted.
//   2. Simulate partial failure: unit row is soft-deleted (mimics crash before grant).
//   3. Player retries BuyUnitAsync → SpendGemsAsync returns AlreadyProcessed.
//   4. Assert: exactly ONE gem spend row in ledger, unit ends up owned again.
public class BuyUnitIdempotencyTests : IAsyncLifetime
{
    // A test-only unit definition that has a gem price so it can be purchased.
    private const string TestUnitId    = "gen_test_idempotency";
    private const int    TestGemPrice  = 50;

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_idempotency_test")
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
                        ["Admin:PlayerIds:0"]                   = Guid.Empty.ToString(),
                        // Neutralize the startup admin seeder — no Seed:AdminPassword required.
                        ["Seed:AdminPassword"]                  = "",
                    });
                });
                host.ConfigureServices(services =>
                {
                    // Override IUnitDefinitionProvider with a stub that exposes a single
                    // gem-priced unit. This avoids modifying production content files.
                    services.AddSingleton<IUnitDefinitionProvider>(
                        new StubUnitDefinitionProvider(new UnitDefinition
                        {
                            Id          = TestUnitId,
                            Name        = "Idempotency Test General",
                            Description = "Exists only in integration tests.",
                            UnitType    = UnitType.General,
                            Rarity      = ItemRarity.White,
                            BaseAttack  = 10,
                            BaseDefense = 10,
                            Race        = UnitRace.Human,
                            Role        = UnitRole.Tank,
                            Attribute   = UnitAttribute.Strength,
                            LegionBonus = 0,
                            GemPrice    = TestGemPrice,
                        }));
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
    // Recovery test
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuyUnit_LostGrantRecovery_RetryDeliverUnitWithoutDoubleCharge()
    {
        // ---- Seed: player with enough gems ----------------------------------
        Player player;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

            player = Player.Create("idemptest", "idemptest@rota.test", "hash");
            db.Players.Add(player);

            // Grant 100 gems directly into the ledger so the first BuyUnit succeeds.
            db.GemTransactions.Add(GemTransaction.Create(
                player.Id, 100, GemTransactionType.AdminGrant, $"seed:{player.Id}"));

            await db.SaveChangesAsync();
        }

        // ---- Step 1: successful first BuyUnit → gem charged + unit granted --
        using (var scope = _factory.Services.CreateScope())
        {
            var legion = scope.ServiceProvider.GetRequiredService<ILegionService>();
            var result = await legion.BuyUnitAsync(player.Id, TestUnitId);
            result.Success.Should().BeTrue("first BuyUnit should succeed with sufficient gems");
        }

        // Verify: one gem spend row and unit is owned.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

            var spendRows = await db.GemTransactions
                .Where(t => t.PlayerId == player.Id
                         && t.TransactionType == GemTransactionType.UnitPurchase
                         && t.ReferenceId == $"unitbuy:{player.Id}:{TestUnitId}")
                .CountAsync();
            spendRows.Should().Be(1, "first buy must write exactly one spend row");

            var unitOwned = await db.PlayerUnits
                .FirstOrDefaultAsync(u => u.PlayerId == player.Id && u.UnitDefinitionId == TestUnitId);
            unitOwned.Should().NotBeNull();
            unitOwned!.IsDeleted.Should().BeFalse("unit must be owned after first buy");
        }

        // ---- Step 2: simulate lost grant — soft-delete the player_units row --
        // This replicates the crash scenario: gem row in the ledger exists,
        // but the unit was never (or no longer) in player_units.
        using (var scope = _factory.Services.CreateScope())
        {
            var db   = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var unit = await db.PlayerUnits
                .FirstAsync(u => u.PlayerId == player.Id && u.UnitDefinitionId == TestUnitId);
            unit.SoftDelete();
            await db.SaveChangesAsync();
        }

        // Confirm unit is now gone from the player's perspective.
        using (var scope = _factory.Services.CreateScope())
        {
            var db   = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var unit = await db.PlayerUnits
                .FirstOrDefaultAsync(u => u.PlayerId == player.Id
                                       && u.UnitDefinitionId == TestUnitId
                                       && !u.IsDeleted);
            unit.Should().BeNull("soft-delete confirmed — unit appears not owned before retry");
        }

        // ---- Step 3: retry BuyUnit — should recover without double-charging --
        using (var scope = _factory.Services.CreateScope())
        {
            var legion = scope.ServiceProvider.GetRequiredService<ILegionService>();
            var result = await legion.BuyUnitAsync(player.Id, TestUnitId);
            result.Success.Should().BeTrue(
                "retry must succeed: SpendGemsAsync returns AlreadyProcessed and grant re-runs");
        }

        // ---- Assertions ----------------------------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

            // (a) The gem ledger has EXACTLY ONE spend row for this referenceId.
            var spendRows = await db.GemTransactions
                .Where(t => t.PlayerId == player.Id
                         && t.TransactionType == GemTransactionType.UnitPurchase
                         && t.ReferenceId == $"unitbuy:{player.Id}:{TestUnitId}")
                .CountAsync();
            spendRows.Should().Be(1,
                "gems must be charged exactly once — retry must NOT write a second spend row");

            // (b) Unit ends up owned again (restored by UpsertAsync on retry).
            var unit = await db.PlayerUnits
                .FirstOrDefaultAsync(u => u.PlayerId == player.Id
                                       && u.UnitDefinitionId == TestUnitId
                                       && !u.IsDeleted);
            unit.Should().NotBeNull(
                "unit must be re-granted by the retry so the player is not stuck without their purchase");

            // (c) Balance is 100 − 50 = 50 (charged exactly once).
            var gems = scope.ServiceProvider.GetRequiredService<IGemService>();
            var balance = await gems.GetBalanceAsync(player.Id);
            balance.Should().Be(50,
                "balance must reflect a single 50-gem deduction from the original 100-gem seed");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
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

    // Minimal stub for IUnitDefinitionProvider — returns exactly one definition.
    private sealed class StubUnitDefinitionProvider : IUnitDefinitionProvider
    {
        private readonly UnitDefinition _unit;

        public StubUnitDefinitionProvider(UnitDefinition unit) => _unit = unit;

        public UnitDefinition? GetById(string id)
            => id == _unit.Id ? _unit : null;

        public IReadOnlyList<UnitDefinition> GetAll()
            => new[] { _unit };
    }
}
