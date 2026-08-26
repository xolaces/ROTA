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

/// <summary>
/// T43 — verifies the real <see cref="IGuildRepository.BrowseAsync"/> hides the Dev guild
/// ("The Dev Coffee Shop", tag DEV) from the public browse list, and that the exclusion is applied
/// BEFORE paging (so paging stays correct — the hidden row never consumes a page slot).
/// Runs against a real PostgreSQL via Testcontainers (Docker required), mirroring the other guild
/// DB tests. The dev tag is read from GuildConfig via IOptions, so this exercises the production wiring.
/// </summary>
public class DevGuildBrowseTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_devguild_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
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
                        ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                        ["Jwt:PublicKey"] = publicKeyPem,
                        ["Jwt:PrivateKey"] = privateKeyPem,
                        ["Jwt:Issuer"] = "rota-test",
                        ["Jwt:Audience"] = "rota-test",
                        ["Admin:PlayerIds:0"] = Guid.Empty.ToString(),
                        // Neutralize the startup seeders so they don't add rows / require config.
                        ["Seed:AdminPassword"] = "",
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

    private async Task<Guid> SeedGuildAsync(string name, string tag, int memberCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var leader = Player.Create($"leader_{tag}", $"leader_{tag}@rota.test", "hash");
        db.Players.Add(leader);
        var g = Guild.Create(leader.Id, name, tag, "seed", GuildJoinPolicy.Open, 50);
        // Drive the denormalized count up so ordering (member-count desc) is deterministic.
        for (int i = 1; i < memberCount; i++) g.IncrementMemberCount();
        db.Guilds.Add(g);
        await db.SaveChangesAsync();
        return g.Id;
    }

    [Fact]
    public async Task BrowseAsync_ExcludesDevGuild_AndKeepsPagingCorrect()
    {
        // Three normal guilds + the Dev guild (tag DEV). Member counts make ordering deterministic.
        await SeedGuildAsync("Alpha", "ALP", memberCount: 40);
        await SeedGuildAsync("The Dev Coffee Shop", "DEV", memberCount: 30); // hidden
        await SeedGuildAsync("Bravo", "BRV", memberCount: 20);
        await SeedGuildAsync("Charlie", "CHR", memberCount: 10);

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGuildRepository>();

        // Full list: the dev guild must be absent.
        var all = await repo.BrowseAsync(query: null, page: 1, pageSize: 25);
        all.Select(e => e.Guild.Tag).Should().BeEquivalentTo(new[] { "ALP", "BRV", "CHR" });
        all.Should().NotContain(e => e.Guild.Tag == "DEV");

        // Paging correctness: with pageSize 2, page 1 = ALP+BRV (NOT ALP+DEV), page 2 = CHR.
        var page1 = await repo.BrowseAsync(query: null, page: 1, pageSize: 2);
        page1.Select(e => e.Guild.Tag).Should().Equal("ALP", "BRV");
        var page2 = await repo.BrowseAsync(query: null, page: 2, pageSize: 2);
        page2.Select(e => e.Guild.Tag).Should().Equal("CHR");

        // Searching for the dev tag must still hide it.
        var searchDev = await repo.BrowseAsync(query: "DEV", page: 1, pageSize: 25);
        searchDev.Should().BeEmpty("the dev guild is hidden even when searched for by tag");
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
