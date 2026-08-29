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
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// Drives the summon catalogue over HTTP against the REAL content files, which is the half a unit
// test cannot reach: routing, the auth filter, JSON serialization, and — the actual risk here —
// whether /api/raids/catalogue collides with the pre-existing /api/raids/{activeRaidId}.
//
// That collision is not hypothetical. RaidController already binds a bare {activeRaidId} to a Guid
// with no route constraint, so "catalogue" is a candidate match for it; the endpoints only coexist
// because ASP.NET ranks a literal segment above a parameter one. Worth pinning rather than assuming.
public class RaidCatalogueEndpointTests : IAsyncLifetime
{
    private const string Password = "Test-Password-1!";

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_raidcatalogue_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // JWT keys come from TestJwtKeys via environment variables — see that file for why
        // AddInMemoryCollection cannot carry them.
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

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RotaDbContext>().Database.MigrateAsync();

            var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            await players.CreateAsync(Player.Create(
                "catalogue_tester", "catalogue_tester@rota.test",
                BCrypt.Net.BCrypt.HashPassword(Password, 12)));
        }

        _client = _factory.CreateClient();
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = "catalogue_tester@rota.test", Password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
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

    // ── routing ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Catalogue_IsReachable_AndDoesNotBindToTheActiveRaidRoute()
    {
        var res = await _client.GetAsync("/api/raids/catalogue");

        res.StatusCode.Should().Be(HttpStatusCode.OK,
            "a literal segment must outrank the bare {activeRaidId} parameter — otherwise "
            + "\"catalogue\" is parsed as a Guid and this 400s");

        var all = await res.Content.ReadFromJsonAsync<List<RaidPreviewResponse>>();
        all.Should().NotBeNullOrEmpty("raids.json ships 25 raids and the content root is loaded");
    }

    [Fact]
    public async Task Catalogue_RequiresAuthentication()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/raids/catalogue"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── the real content ──────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_CarriesTheNumbersTheSummonScreenNeeds()
    {
        var res = await _client.GetAsync("/api/raids/catalogue/raid_ironcolossus");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var p = await res.Content.ReadFromJsonAsync<RaidPreviewResponse>();

        // Pinned against raids.json rather than a fixture: if content changes, this should say so.
        p!.Name.Should().Be("The Iron Colossus");
        p.Tier.Should().Be("World");
        p.BaseHp.Should().Be(100000);
        p.PersonalHp.Should().Be(500);
        p.TimerHours.Should().Be(48);
        p.Difficulties.Should().Contain(new[] { "Normal", "Hard", "Legendary", "Nightmare" });
    }

    [Fact]
    public async Task Preview_UnknownRaid_Is404()
    {
        (await _client.GetAsync("/api/raids/catalogue/raid_does_not_exist"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Loot_ComesBackAsAscendingBrackets_WithResolvedNames()
    {
        var res = await _client.GetAsync("/api/raids/catalogue/raid_ironcolossus/loot?difficulty=Nightmare");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var loot = await res.Content.ReadFromJsonAsync<RaidLootPreviewResponse>();

        loot!.Difficulty.Should().Be("Nightmare");
        loot.Brackets.Should().HaveCountGreaterThan(1);
        loot.Brackets.Select(b => b.ContributionPercent)
            .Should().BeInAscendingOrder();

        var top = loot.Brackets.Last();
        top.StatPoints.Should().BeGreaterThan(0, "stat points are most of what a bracket pays");
        top.Drops.Should().NotBeEmpty();
        top.Drops.Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.Name),
            "an unresolved id would render as a blank row");
    }

    [Fact]
    public async Task Loot_DifficultyIsCaseInsensitive()
    {
        var res = await _client.GetAsync("/api/raids/catalogue/raid_ironcolossus/loot?difficulty=nightmare");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<RaidLootPreviewResponse>())!
            .Difficulty.Should().Be("Nightmare", "the response echoes the content's spelling");
    }

    [Fact]
    public async Task Loot_ADifficultyTheTableDoesNotDefine_Is404_NotAnEmptyList()
    {
        var res = await _client.GetAsync("/api/raids/catalogue/raid_ironcolossus/loot?difficulty=Impossible");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "\"no such tier\" and \"drops nothing\" are different answers");
    }

    [Fact]
    public async Task Loot_HigherBracketsPayMore_WhichIsTheWholePointOfShowingThem()
    {
        var res = await _client.GetAsync("/api/raids/catalogue/raid_ironcolossus/loot?difficulty=Nightmare");
        var loot = await res.Content.ReadFromJsonAsync<RaidLootPreviewResponse>();

        var first = loot!.Brackets.First();
        var last  = loot.Brackets.Last();

        last.StatPoints.Should().BeGreaterThan(first.StatPoints,
            "contribution is the only part of raid loot a player can influence; if the top bracket "
            + "did not pay more, showing the ladder would be theatre");
    }
}
