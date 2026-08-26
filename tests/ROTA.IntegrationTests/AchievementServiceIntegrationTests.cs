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

// TICKET 46 — end-to-end achievement award against the REAL DB through the real service + DI graph:
// crossing a threshold awards exactly one ledger row (idempotent on re-eval), and the profile's
// TotalAchievementPoints reflects the ledger SUM.
public class AchievementServiceIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_achievement_e2e_test")
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

    [Fact]
    public async Task CrossingThreshold_AwardsOnce_ProfileReflectsLedgerSum()
    {
        Player player;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            player = Player.Create("acheen", "acheen@rota.test", "hash");
            db.Players.Add(player);
            await db.SaveChangesAsync();
        }

        // Record 10 raid completions → crosses the "Slayer" (ach_raids_10, +10 AP) threshold.
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAchievementService>();
            await svc.RecordProgressAsync(player.Id, AchievementMetric.RaidCompletions, 10);
            await svc.EvaluateCompletionsAsync(player.Id);
            // Re-evaluate twice more — must not double-award.
            await svc.EvaluateCompletionsAsync(player.Id);
            await svc.EvaluateCompletionsAsync(player.Id);
        }

        // Exactly one award row for ach_raids_10; ach_raids_100 (threshold 100) is NOT awarded.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var awards = await db.AchievementAwards.Where(a => a.PlayerId == player.Id).ToListAsync();
            awards.Should().ContainSingle(a => a.AchievementId == "ach_raids_10");
            awards.Should().NotContain(a => a.AchievementId == "ach_raids_100");

            var progress = await db.AchievementProgress.AsNoTracking()
                .FirstAsync(p => p.PlayerId == player.Id && p.AchievementId == "ach_raids_10");
            progress.IsCompleted.Should().BeTrue();
            progress.CompletedAt.Should().NotBeNull();
        }

        // The profile's TotalAchievementPoints reflects the ledger SUM (10).
        using (var scope = _factory.Services.CreateScope())
        {
            var players = scope.ServiceProvider.GetRequiredService<IPlayerService>();
            var profile = await players.GetProfileAsync(player.Id);
            profile.Should().NotBeNull();
            profile!.TotalAchievementPoints.Should().Be(10);
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
}
