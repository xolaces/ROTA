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

// HOW TO VERIFY THIS TEST REQUIRES THE ATOMIC UPDATE:
// In BetaKeyRepository.TryRedeemAsync, replace the WHERE condition with unconditional SET and
// remove "AND is_redeemed = false". Run this test — both registrations succeed, two players are
// created, and the assertion "exactly one player created" fails. Restore the condition to pass.
public class BetaKeyConcurrencyTests : IAsyncLifetime
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
            .WithDatabase("rota_beta_test")
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
                        // Enable the beta gate for this test.
                        ["BetaGate:Enabled"]                    = "true",
                        // Neutralize the startup admin seeder so this fixture stays hermetic against a
                        // developer's Seed:AdminPassword user-secret (the seeder would otherwise query
                        // players during host startup, before migrations are applied).
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

    // -----------------------------------------------------------------------
    // Concurrency test
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentRegistrations_SameKey_ExactlyOnePlayerCreated()
    {
        // ---- Seed a single-use beta key ------------------------------------
        const string sharedKey = "ROTA-TEST-CONC-URRY";
        using (var scope = _factory.Services.CreateScope())
        {
            var betaKeyRepo = scope.ServiceProvider.GetRequiredService<IBetaKeyRepository>();
            var betaKey     = BetaKey.Create(sharedKey, createdBy: null);
            await betaKeyRepo.CreateAsync(betaKey);
        }

        // ---- Two concurrent RegisterAsync calls with the same key ----------
        // Each runs in its own DI scope = its own RotaDbContext = its own PG connection.
        // The atomic conditional UPDATE in TryRedeemAsync serialises these at the DB level:
        // exactly one sees rowsAffected=1 and proceeds to create a player.
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        var authService1 = scope1.ServiceProvider.GetRequiredService<IAuthService>();
        var authService2 = scope2.ServiceProvider.GetRequiredService<IAuthService>();

        var req1 = new RegisterRequest
        {
            Username = "racer1",
            Email    = "racer1@rota.test",
            Password = "Secure1Pass",
            BetaKey  = sharedKey,
        };
        var req2 = new RegisterRequest
        {
            Username = "racer2",
            Email    = "racer2@rota.test",
            Password = "Secure1Pass",
            BetaKey  = sharedKey,
        };

        var results = await Task.WhenAll(
            Task.Run(async () => await authService1.RegisterAsync(req1, "127.0.0.1")),
            Task.Run(async () => await authService2.RegisterAsync(req2, "127.0.0.2")));

        // ---- Assertions ----------------------------------------------------

        // 1. Exactly one registration succeeded.
        var successes = results.Count(r => r is not null);
        successes.Should().Be(1, "exactly one concurrent registration should succeed for a single-use key");

        // 2. Exactly one player was created in the DB.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var playerCount = await db.Players.CountAsync(p =>
                (p.Username == "racer1" || p.Username == "racer2") && !p.IsDeleted);
            playerCount.Should().Be(1, "the single-use key must result in exactly one player row");
        }

        // 3. The beta key is now redeemed.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var key = await db.BetaKeys.FirstAsync(k => k.Key == sharedKey);
            key.IsRedeemed.Should().BeTrue("the key must be marked as redeemed after successful use");
            key.RedeemedByPlayerId.Should().NotBeNull();
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
