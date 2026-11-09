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

// The player-facing half of the revoke-reason fix, end to end over HTTP.
//
// Before it, every revoked refresh token presented again was read as a replayed rotation: the server
// revoked EVERY session and filed TokenReplayDetected. So the oldest device, pushed out by a fourth
// login, logged the player out of the new one the moment it refreshed; and a stale tab after a logout
// did the same to every other device. Only a genuinely rotated token should do that.
//
// Each test gets fresh containers (xUnit builds a new instance per test), so the per-IP auth rate
// limit (10 per 60s per endpoint) resets between them. The busiest test makes 4 logins.
public class RefreshTokenSessionFlowTests : IAsyncLifetime
{
    private const string Password = "Test-Password-1!";
    private const string Username = "sessions_player";

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_refresh_flow_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // JWT keys come from TestJwtKeys (environment variables at assembly load); see ModerationEndpointTests.
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
                        ["Seed:AdminPassword"]                  = "",
                    });
                });
            });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            await db.Database.MigrateAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            await players.CreateAsync(Player.Create(Username, $"{Username}@rota.test",
                BCrypt.Net.BCrypt.HashPassword(Password, 12)));
        }
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

    /// <summary>One login = one device's session.</summary>
    private async Task<AuthResponse> LoginAsync()
    {
        using var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = $"{Username}@rota.test", Password = Password });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = refreshToken });
    }

    private async Task<bool> ReplayWasFiledAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        return await db.AuditLogs.AnyAsync(a => a.Action == "TokenReplayDetected");
    }

    [Fact]
    public async Task OldestDevice_PushedOutBySessionCap_DoesNotLogOutTheNewOnes()
    {
        // Cap is 3: the fourth login pushes out the first.
        var first  = await LoginAsync();
        var second = await LoginAsync();
        await LoginAsync();
        var fourth = await LoginAsync();

        (await RefreshAsync(first.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the pushed-out session is over");

        (await RefreshAsync(fourth.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the device that just logged in must survive the old one waking up");
        (await RefreshAsync(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReplayWasFiledAsync()).Should().BeFalse("nothing here was a theft");
    }

    [Fact]
    public async Task StaleTabAfterLogout_DoesNotLogOutOtherDevices()
    {
        var laptop = await LoginAsync();
        var phone  = await LoginAsync();

        using (var client = _factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", laptop.AccessToken);
            var res = await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest { RefreshToken = laptop.RefreshToken });
            res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await RefreshAsync(laptop.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a logged-out session never refreshes");
        (await RefreshAsync(phone.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "logging out on one device must not end the others");
        (await ReplayWasFiledAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ReplayedRotatedToken_StillRevokesTheWholeFamily()
    {
        var device = await LoginAsync();

        var rotated = await RefreshAsync(device.RefreshToken);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = (await rotated.Content.ReadFromJsonAsync<AuthResponse>())!;

        (await RefreshAsync(device.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a consumed token is single-use");
        (await RefreshAsync(next.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "replaying a rotated token is the theft signal, so the live token goes too");
        (await ReplayWasFiledAsync()).Should().BeTrue();
    }
}
