using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// Two players sending each other a friend request at the same moment must end as ONE accepted
// friendship, never two rows and never two pending requests.
public class FriendRequestConcurrencyTests : IAsyncLifetime
{
    private const string Password = "Test-Password-1!";
    private const int Pairs = 20;

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_friend_race_test").WithUsername("test").WithPassword("test").Build();
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
                        ["RateLimitConfig:AuthRequestsPerWindow"]   = "100000",
                        ["RateLimitConfig:PlayerRequestsPerWindow"] = "100000",
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

    private async Task<Guid> CreatePlayerAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var p = Player.Create(username, $"{username}@rota.test", BCrypt.Net.BCrypt.HashPassword(Password, 4));
        await players.CreateAsync(p);
        return p.Id;
    }

    private async Task<HttpClient> SignInAsync(string username)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = $"{username}@rota.test", Password = Password });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    [Fact]
    public async Task CrossedRequests_SentAtOnce_EndAsOneAcceptedFriendship()
    {
        for (var i = 0; i < Pairs; i++)
        {
            var a = $"fr_a{i}";
            var b = $"fr_b{i}";
            var aId = await CreatePlayerAsync(a);
            var bId = await CreatePlayerAsync(b);
            using var ca = await SignInAsync(a);
            using var cb = await SignInAsync(b);

            var results = await Task.WhenAll(
                ca.PostAsJsonAsync("/api/social/friends/request", new FriendTargetRequest { Target = b }),
                cb.PostAsJsonAsync("/api/social/friends/request", new FriendTargetRequest { Target = a }));

            foreach (var r in results)
                ((int)r.StatusCode).Should().BeLessThan(500, $"pair {i}: a crossed request must not error ({r.StatusCode})");

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var active = await db.Friendships.IgnoreQueryFilters().AsNoTracking()
                .Where(f => !f.IsDeleted &&
                            ((f.RequesterId == aId && f.AddresseeId == bId) ||
                             (f.RequesterId == bId && f.AddresseeId == aId)))
                .ToListAsync();

            active.Should().ContainSingle($"pair {i}: exactly one live row for the pair");
            active[0].Status.Should().Be(FriendshipStatus.Accepted, $"pair {i}: both asked, so they are friends");
        }
    }
}
