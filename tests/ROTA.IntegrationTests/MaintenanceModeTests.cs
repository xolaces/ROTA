using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

// Maintenance mode through the real pipeline: Redis-held switch, the middleware's place after
// authentication (an Admin token must pass), and the paths that stay open while the game is closed.
public class MaintenanceModeTests : IAsyncLifetime
{
    private const string Password = "Test-Password-1!";
    private const string Line = "Back at nine.";

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _anon = null!;
    private HttpClient _player = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_maintenance_test").WithUsername("test").WithPassword("test").Build();
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

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RotaDbContext>().Database.MigrateAsync();

        _anon = _factory.CreateClient();
        await CreatePlayerAsync("maint_player", admin: false);
        await CreatePlayerAsync("maint_admin", admin: true);
        _player = await SignInAsync("maint_player");
        _admin  = await SignInAsync("maint_admin");
    }

    public async Task DisposeAsync()
    {
        _anon?.Dispose(); _player?.Dispose(); _admin?.Dispose();
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

    private async Task CreatePlayerAsync(string username, bool admin)
    {
        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var p = Player.Create(username, $"{username}@rota.test", BCrypt.Net.BCrypt.HashPassword(Password, 12));
        if (admin) p.GrantRole(PlayerRoles.Admin);
        await players.CreateAsync(p);
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

    // Set through the DI singleton so its five-second cache is reset with the write.
    private Task SetMaintenance(bool on)
        => _factory.Services.GetRequiredService<IServiceNoticeStore>().SetMaintenanceAsync(on, on ? Line : null);

    [Fact]
    public async Task WhileClosed_PlayersGet503_TheOpenPathsAnswer_AndAnAdminGetsIn()
    {
        await SetMaintenance(true);
        try
        {
            // A signed-in player is turned away with the owner's line.
            var res = await _player.GetAsync("/api/players/me");
            res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            res.Headers.RetryAfter.Should().NotBeNull();
            using (var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()))
            {
                body.RootElement.GetProperty("maintenance").GetBoolean().Should().BeTrue();
                body.RootElement.GetProperty("message").GetString().Should().Be(Line);
            }

            // The status endpoint and health stay open; status reports the switch.
            var status = await _anon.GetAsync("/api/status");
            status.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
                body.RootElement.GetProperty("maintenance").GetBoolean().Should().BeTrue();
            (await _anon.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

            // Login stays open (an admin has to be able to sign in to check the build).
            using (var fresh = await SignInAsync("maint_player")) { }

            // An Admin token passes: the gate runs after authentication.
            (await _admin.GetAsync("/api/players/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await SetMaintenance(false);
        }

        (await _player.GetAsync("/api/players/me")).StatusCode.Should().Be(HttpStatusCode.OK,
            "switching maintenance off reopens the game at once");
    }
}
