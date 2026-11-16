using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// Raid HP scales with size (owner, 2026-11-16). Run through the real host so the multipliers are the
// ones bound from appsettings.json, and the summoned raid is read back from the database.
public class RaidSizeHpTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_raid_size_hp_test").WithUsername("test").WithPassword("test").Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseContentRoot(FindApiContentRoot());
                host.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["ConnectionStrings:Redis"]             = _redis.GetConnectionString(),
                        ["Seed:AdminPassword"]                  = "",
                    }));
            });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RotaDbContext>().Database.MigrateAsync();
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

    [Fact]
    public void Multipliers_BindFromAppsettings()
    {
        var cfg = _factory.Services.GetRequiredService<IOptions<RaidConfig>>().Value;
        cfg.HpMultiplierFor(RaidSize.Personal).Should().Be(1.0);
        cfg.HpMultiplierFor(RaidSize.Small).Should().Be(1.0);
        cfg.HpMultiplierFor(RaidSize.Medium).Should().Be(2.5);
        cfg.HpMultiplierFor(RaidSize.Large).Should().Be(5.0);
        cfg.HpMultiplierFor(RaidSize.Titanic).Should().Be(20.0);
    }

    // Per-chapter base HP rebase (owner, 2026-11-16): every campaign raid is listed, with one multiplier
    // per chapter; World raids (MaxHp 0 in content), guild raids, Gauntlet stages and anything unlisted
    // stay at 1. The provider also registers guild and Gauntlet definitions, so the list is Standard only.
    [Theory]
    [InlineData("raid_c1z1b",          125.0)]
    [InlineData("raid_c2z3b",          400.0)]
    [InlineData("raid_c3z0b",          400.0)]
    [InlineData("raid_c4z3b",          325.0)]
    [InlineData("raid_c5z0b",          250.0)]
    [InlineData("raid_c6z4b",          200.0)]
    [InlineData("raid_lastwatch_lamp", 160.0)]
    [InlineData("raid_ironcolossus",     1.0)]
    [InlineData("raid_not_a_raid",       1.0)]
    public void BaseHpMultipliers_BindFromAppsettings(string raidId, double expected)
    {
        var cfg = _factory.Services.GetRequiredService<IOptions<RaidConfig>>().Value;
        cfg.BaseHpMultiplierFor(raidId).Should().Be(expected);
    }

    [Fact]
    public void EveryCampaignRaid_HasABaseHpMultiplier()
    {
        var cfg  = _factory.Services.GetRequiredService<IOptions<RaidConfig>>().Value;
        var defs = _factory.Services.GetRequiredService<IRaidDefinitionProvider>().GetAll()
            .Where(d => d.Tier == "Standard" && d.BaseHp > 0).Select(d => d.Id).ToList();
        defs.Should().NotBeEmpty();
        cfg.BaseHpMultiplierByRaid.Keys.Should().BeEquivalentTo(defs);
    }

    [Fact]
    public async Task SummonedMediumHardRaid_HasBaseHp_TimesRaidMultiplier_TimesHard_TimesMedium()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var player = Player.Create("size_hp", "size_hp@rota.test", "hash");
        await sp.GetRequiredService<IPlayerRepository>().CreateAsync(player);
        var def = sp.GetRequiredService<IRaidDefinitionProvider>().GetById("raid_c1z1b");
        def.Should().NotBeNull();

        var result = await sp.GetRequiredService<IRaidService>()
            .SummonRaidAsync(player.Id, "raid_c1z1b", RaidDifficulty.Hard, RaidSize.Medium);

        result.Success.Should().BeTrue(result.FailureReason);
        var cfg = _factory.Services.GetRequiredService<IOptions<RaidConfig>>().Value;
        cfg.BaseHpMultiplierFor("raid_c1z1b").Should().Be(125.0);
        long expected = (long)(def!.BaseHp * 125.0 * 1.4 * 2.5);
        result.Response!.MaxHp.Should().Be(expected);

        using var read = _factory.Services.CreateScope();
        var stored = await read.ServiceProvider.GetRequiredService<RotaDbContext>().ActiveRaids
            .AsNoTracking().FirstAsync(r => r.Id == result.Response.ActiveRaidId);
        stored.MaxHp.Should().Be(expected);
        stored.CurrentHp.Should().Be(expected);
        stored.Size.Should().Be(RaidSize.Medium);
    }
}
