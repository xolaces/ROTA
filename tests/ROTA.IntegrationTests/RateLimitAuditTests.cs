using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// A rate-limit breach is audited ONCE per key per window, not once per rejected request.
//
// THE DEFECT THIS PINS. The 429 path is deliberately cheap — that is the point of rate limiting —
// but auditing every rejection made it expensive for the server and free for the caller: one DI
// scope and one durable audit_log INSERT per request, unbounded, and on the anon branch with no
// authentication at all. A scanner, or a client stuck in a retry loop, wrote rows at line rate
// through the one path built to shed load cheaply.
//
// Deduping loses nothing: the signal is "this key breached the limit during this window", which the
// first row already carries.
public class RateLimitAuditTests : IAsyncLifetime
{
    private const int AuthLimit = 2;

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_ratelimit_audit_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
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
                        // A tiny ceiling and a long window, so every request after the first few is a
                        // rejection and they all land in ONE window.
                        ["RateLimitConfig:AuthRequestsPerWindow"] = AuthLimit.ToString(),
                        ["RateLimitConfig:WindowSeconds"]         = "60",
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

    private async Task<int> BreachAuditRowCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .CountAsync(a => a.Action.StartsWith("RateLimitIp"));
    }

    [Fact]
    public async Task ManyRejectedRequests_WriteExactlyOneBreachAuditRow()
    {
        var client = _factory.CreateClient();

        // Credentials do not matter: the limiter runs ahead of the endpoint, so once the ceiling is
        // passed every request is rejected before anything looks at the body.
        var body = new { username = "nobody", password = "wrong-password" };

        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < AuthLimit + 20; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", body);
            statuses.Add(response.StatusCode);
        }

        statuses.Count(s => s == HttpStatusCode.TooManyRequests).Should().BeGreaterThan(10,
            "the ceiling is tiny, so most of these must have been rejected");

        (await BreachAuditRowCountAsync()).Should().Be(1,
            "twenty-odd rejections in one window are one breach, and writing a row per rejection let a "
            + "caller drive unbounded inserts through the cheapest path in the API");
    }
}
