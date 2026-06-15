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

// System 16 Slice 6 — the economy-idempotency proof for the token shop, against REAL Postgres
// ledgers. Two scenarios the spec calls out:
//   (1) Buy-twice charges once: a repeatable GemBundle bought twice must write exactly ONE
//       currency-spend row AND grant the gems exactly once (gem ledger de-dupes the referenceId).
//   (2) Currency isolation: a Pitchfork-priced entry attempted with only a Token balance must
//       return InsufficientTokens with NO debit and NO grant — Token and Pitchfork are never
//       conflated.
//
// HOW TO VERIFY THIS TEST CATCHES THE BUG:
//   - In GauntletCurrencyRepository.SpendAsync, drop the ReferenceExists/unique-violation guard
//     (always insert + return Charged): scenario (1) finds TWO spend rows → fails.
//   - In GauntletService.BuyFromShopAsync, pass GauntletCurrency.Token instead of entry.Currency:
//     scenario (2) would charge the Token balance and succeed → fails the no-debit assertion.
public class GauntletShopIdempotencyTests : IAsyncLifetime
{
    private const string GemBundleEntryId = "shop_test_gembundle";
    private const int    GemBundleAmount  = 250;
    private const int    GemBundlePrice   = 50;   // Token

    private const string PitchforkGearEntryId = "shop_test_pitchfork_gear";
    private const string PitchforkGearPayload = "gear_test_shop";
    private const int    PitchforkGearPrice   = 15; // Pitchfork

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_gauntletshop_test")
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
                        ["Seed:AdminPassword"]                  = "",
                    });
                });
                host.ConfigureServices(services =>
                {
                    // Override the shop catalogue with a deterministic test stub so we don't depend
                    // on the shipped catalogue's exact ids/prices. A Gear def stub backs the
                    // Pitchfork entry's payloadId (own-once kinds need a resolvable payloadId).
                    services.AddSingleton<IGearDefinitionProvider>(
                        new StubGearDefinitionProvider(new GearDefinition
                        {
                            Id   = PitchforkGearPayload,
                            Name = "Shop Test Gear",
                            Slot = "Head",
                            Rarity = ItemRarity.Orange,
                        }));
                    services.AddSingleton<IGauntletShopProvider>(new StubShopProvider());
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

    [Fact]
    public async Task BuyShop_GemBundle_Twice_ChargesOnce_GrantsOnce()
    {
        Player player;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            player = Player.Create("shopidemp", "shopidemp@rota.test", "hash");
            db.Players.Add(player);

            // Seed a 1000-Token balance via a credit row on the currency ledger.
            db.GauntletCurrencyTransactions.Add(GauntletCurrencyTransaction.Create(
                player.Id, GauntletCurrency.Token, 1000,
                GauntletCurrencyTransactionType.RankReward, $"seed:token:{player.Id}"));
            await db.SaveChangesAsync();
        }

        // ---- Buy #1 → success, charged + granted -----------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var gauntlet = scope.ServiceProvider.GetRequiredService<IGauntletService>();
            var r1 = await gauntlet.BuyFromShopAsync(player.Id, GemBundleEntryId);
            r1.Success.Should().BeTrue("first buy succeeds with a sufficient Token balance");
            r1.AlreadyCharged.Should().BeFalse();
        }

        // ---- Buy #2 (same entry, same referenceId) → AlreadyCharged re-grant --
        using (var scope = _factory.Services.CreateScope())
        {
            var gauntlet = scope.ServiceProvider.GetRequiredService<IGauntletService>();
            var r2 = await gauntlet.BuyFromShopAsync(player.Id, GemBundleEntryId);
            r2.Success.Should().BeTrue("second buy is an idempotent success");
            r2.AlreadyCharged.Should().BeTrue("the spend row already exists → AlreadyCharged");
        }

        // ---- Assert against REAL ledgers -------------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var db  = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var refId = $"gauntletshop:{player.Id}:{GemBundleEntryId}";

            // (a) EXACTLY ONE currency-spend (ShopPurchase) row for this reference.
            var spendRows = await db.GauntletCurrencyTransactions
                .Where(t => t.PlayerId == player.Id
                         && t.Currency == GauntletCurrency.Token
                         && t.TransactionType == GauntletCurrencyTransactionType.ShopPurchase
                         && t.ReferenceId == refId)
                .CountAsync();
            spendRows.Should().Be(1, "buy-twice must charge exactly once");

            // (b) Token balance debited exactly once: 1000 − 50 = 950.
            var tokenBalance = await db.GauntletCurrencyTransactions
                .Where(t => t.PlayerId == player.Id && t.Currency == GauntletCurrency.Token)
                .SumAsync(t => (long)t.Amount);
            tokenBalance.Should().Be(1000 - GemBundlePrice, "exactly one 50-Token debit");

            // (c) Gems granted exactly once (gem ledger de-dupes the shop referenceId).
            var gemGrantRows = await db.GemTransactions
                .Where(t => t.PlayerId == player.Id
                         && t.TransactionType == GemTransactionType.GauntletShopReward
                         && t.ReferenceId == refId)
                .CountAsync();
            gemGrantRows.Should().Be(1, "the GemBundle must be granted exactly once across both buys");

            var gems = scope.ServiceProvider.GetRequiredService<IGemService>();
            (await gems.GetBalanceAsync(player.Id)).Should().Be(GemBundleAmount,
                "gem balance reflects a single 250-gem grant, not 500");
        }
    }

    [Fact]
    public async Task BuyShop_PitchforkPriced_WithOnlyTokenBalance_InsufficientTokens_NoDebit_NoGrant()
    {
        Player player;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            player = Player.Create("isolation", "isolation@rota.test", "hash");
            db.Players.Add(player);

            // A FAT Token balance, but ZERO Pitchfork — the Pitchfork-priced entry must still fail.
            db.GauntletCurrencyTransactions.Add(GauntletCurrencyTransaction.Create(
                player.Id, GauntletCurrency.Token, 100_000,
                GauntletCurrencyTransactionType.RankReward, $"seed:token:{player.Id}"));
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var gauntlet = scope.ServiceProvider.GetRequiredService<IGauntletService>();
            var result = await gauntlet.BuyFromShopAsync(player.Id, PitchforkGearEntryId);

            result.InsufficientTokens.Should().BeTrue(
                "a Pitchfork-priced entry cannot be paid with a Token balance — currency isolation");
            result.Success.Should().BeFalse();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

            // No debit on EITHER currency.
            var anySpend = await db.GauntletCurrencyTransactions
                .AnyAsync(t => t.PlayerId == player.Id
                            && t.TransactionType == GauntletCurrencyTransactionType.ShopPurchase);
            anySpend.Should().BeFalse("the failed buy must not write any spend row");

            var tokenBalance = await db.GauntletCurrencyTransactions
                .Where(t => t.PlayerId == player.Id && t.Currency == GauntletCurrency.Token)
                .SumAsync(t => (long)t.Amount);
            tokenBalance.Should().Be(100_000, "the Token balance is untouched");

            // No gear granted.
            var gearOwned = await db.PlayerGear
                .AnyAsync(g => g.PlayerId == player.Id && g.GearDefinitionId == PitchforkGearPayload && !g.IsDeleted);
            gearOwned.Should().BeFalse("no payload may be granted on a failed buy");
        }
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

    // Deterministic two-entry catalogue: a Token GemBundle (repeatable) + a Pitchfork Gear (own-once).
    private sealed class StubShopProvider : IGauntletShopProvider
    {
        private readonly List<GauntletShopEntry> _entries = new()
        {
            new GauntletShopEntry
            {
                Id = GemBundleEntryId, RewardKind = GauntletShopRewardKind.GemBundle,
                PayloadId = "", Amount = GemBundleAmount,
                Currency = GauntletCurrency.Token, Price = GemBundlePrice, MaxOwned = null,
            },
            new GauntletShopEntry
            {
                Id = PitchforkGearEntryId, RewardKind = GauntletShopRewardKind.Gear,
                PayloadId = PitchforkGearPayload,
                Currency = GauntletCurrency.Pitchfork, Price = PitchforkGearPrice, MaxOwned = 1,
            },
        };

        public IReadOnlyList<GauntletShopEntry> GetAll() => _entries;
        public GauntletShopEntry? GetById(string id) => _entries.FirstOrDefault(e => e.Id == id);
    }

    private sealed class StubGearDefinitionProvider : IGearDefinitionProvider
    {
        private readonly GearDefinition _gear;
        public StubGearDefinitionProvider(GearDefinition gear) => _gear = gear;
        public GearDefinition? GetById(string id) => id == _gear.Id ? _gear : null;
        public IReadOnlyList<GearDefinition> GetAll() => new[] { _gear };
        public IReadOnlyList<GearDefinition> GetBySlot(string slot)
            => _gear.Slot == slot ? new[] { _gear } : Array.Empty<GearDefinition>();
    }
}
