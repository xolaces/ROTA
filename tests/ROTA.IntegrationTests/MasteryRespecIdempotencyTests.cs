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

// System 22 Phase A Slice 3 — the paid re-spec economy-idempotency proof, against the REAL gem ledger.
// The weekly cap store is overridden with a NO-OP so the gem-ledger week-bucket referenceId is the
// SOLE guard (this is exactly the "Redis flushed" recovery scenario the design's hard backstop covers):
// two paid re-specs in the same ISO week must charge gems EXACTLY ONCE.
//
// HOW TO VERIFY THIS TEST CATCHES THE BUG: in GemService.SpendGemsAsync, drop the
// ReferenceExists/unique-index guard (always insert + return Charged) → the second swap charges again
// → this test finds TWO MasteryRespec rows and a double debit → fails.
public class MasteryRespecIdempotencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_masteryrespec_test")
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
                        ["MasteryConfig:RespecGemCost"]         = "150",
                    });
                });
                host.ConfigureServices(services =>
                {
                    // Override the weekly cap with a no-op → the gem-ledger week refId is the sole guard.
                    services.AddScoped<IMasteryRespecCapStore, NoOpCapStore>();
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
    public async Task PaidRespec_TwiceSameWeek_ChargesGemsExactlyOnce()
    {
        const int gemSeed = 1000;
        const int respecCost = 150;
        Player player;
        string monthRef;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            player = Player.Create("respecidemp", "respecidemp@rota.test", "hash");
            db.Players.Add(player);

            // Seed a gem balance.
            db.GemTransactions.Add(GemTransaction.Create(
                player.Id, gemSeed, GemTransactionType.AdminGrant, $"seed:gems:{player.Id}"));

            // Pre-consume the free paths for BOTH targets so each swap is forced onto the paid path:
            // unlock:Bulwark, unlock:Hoard, and the monthly free swap.
            monthRef = $"respec:free:{player.Id}:{DateTimeOffset.UtcNow.UtcDateTime:yyyy-MM}";
            db.MasteryRespecTransactions.Add(MasteryRespecTransaction.Create(
                player.Id, MasteryRespecKind.NewAncientUnlock, null, MasteryAncient.Bulwark, 0,
                $"respec:unlock:{player.Id}:Bulwark"));
            db.MasteryRespecTransactions.Add(MasteryRespecTransaction.Create(
                player.Id, MasteryRespecKind.NewAncientUnlock, null, MasteryAncient.Hoard, 0,
                $"respec:unlock:{player.Id}:Hoard"));
            db.MasteryRespecTransactions.Add(MasteryRespecTransaction.Create(
                player.Id, MasteryRespecKind.FreeMonthly, null, MasteryAncient.Wrath, 0, monthRef));

            await db.SaveChangesAsync();
        }

        // ---- Paid swap #1 (Bulwark) → charged --------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IMasteryService>();
            var r1 = await svc.RespecAsync(player.Id, MasteryAncient.Bulwark);
            r1.Success.Should().BeTrue("the first paid swap succeeds with a sufficient gem balance");
            r1.Kind.Should().Be("Paid");
            r1.GemSpent.Should().Be(respecCost);
        }

        // ---- Paid swap #2 (Hoard, same ISO week) → gem ledger de-dupes -------
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IMasteryService>();
            var r2 = await svc.RespecAsync(player.Id, MasteryAncient.Hoard);
            r2.Success.Should().BeFalse("the same-week paid charge is already on the gem ledger");
            r2.FailureCode.Should().Be(MasteryRespecFailureCode.WeeklyCapReached);
        }

        // ---- Assert against the REAL gem ledger ------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

            var respecGemRows = await db.GemTransactions
                .Where(t => t.PlayerId == player.Id && t.TransactionType == GemTransactionType.MasteryRespec)
                .CountAsync();
            respecGemRows.Should().Be(1, "two same-week paid swaps must charge gems exactly once");

            var gems = scope.ServiceProvider.GetRequiredService<IGemService>();
            (await gems.GetBalanceAsync(player.Id)).Should().Be(gemSeed - respecCost,
                "exactly one 150-gem debit");

            // The first swap applied (pledge = Bulwark); the second never swapped.
            var refreshed = await db.Players.AsNoTracking().FirstAsync(p => p.Id == player.Id);
            refreshed.ActivePledgeAncient.Should().Be(MasteryAncient.Bulwark);
        }
    }

    private sealed class NoOpCapStore : IMasteryRespecCapStore
    {
        public Task<bool> IsPaidWeeklyUsedAsync(Guid playerId, CancellationToken ct = default) => Task.FromResult(false);
        public Task MarkPaidWeeklyUsedAsync(Guid playerId, CancellationToken ct = default) => Task.CompletedTask;
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
