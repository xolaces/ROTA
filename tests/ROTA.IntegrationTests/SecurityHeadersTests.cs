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

// Every response carries the security headers, including the ones no controller produces.
//
// THE GAP THIS PINS. The API shipped setting none of these. HSTS was configured and CORS was a
// correct explicit allowlist, which is what made the absence easy to miss -- both are about who may
// TALK to the API, and neither says anything about what a browser may DO with a response it already
// holds. A JSON body that reflects any user-controlled bytes is one MIME sniff away from being
// executed as HTML, and nothing stopped the API being framed.
//
// These assert on the pipeline, not the middleware class, so removing the app.UseMiddleware line
// fails them just as loudly as breaking the middleware itself would.
public class SecurityHeadersTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_secheaders_test")
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

    private static void AssertBaselineHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which
            .Should().Be("nosniff", "a sniffed JSON body that reflects user bytes becomes HTML");

        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which
            .Should().Be("DENY", "nothing in this API is meant to be framed");

        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which
            .Should().Be("no-referrer", "URLs carry ids and must not leak to third parties");

        response.Headers.GetValues("Permissions-Policy").Should().ContainSingle().Which
            .Should().Contain("camera=()");
    }

    [Fact]
    public async Task AnAnonymousEndpoint_CarriesTheSecurityHeaders()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertBaselineHeaders(response);
    }

    [Fact]
    public async Task AnUnauthorizedResponse_AlsoCarriesTheSecurityHeaders()
    {
        // The 401 never reaches a controller. Headers set inside MVC would miss it entirely, which is
        // why the middleware runs early in the pipeline rather than near the endpoints.
        var response = await _factory.CreateClient().GetAsync("/api/players/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertBaselineHeaders(response);
    }

    [Fact]
    public async Task ARejectedLogin_AlsoCarriesTheSecurityHeaders()
    {
        // An error path through the auth stack -- the one a scanner hits first and hardest.
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { username = "nobody", password = "wrong-password" });

        AssertBaselineHeaders(response);
    }

    [Fact]
    public async Task TheStrictCsp_IsNotAppliedInDevelopment()
    {
        // Swagger UI is registered in Development and cannot load under default-src 'none'. The tests
        // run in Development, so this pins the exemption deliberately rather than leaving it to be
        // discovered when someone tightens the policy and the docs page goes blank.
        var response = await _factory.CreateClient().GetAsync("/health");

        response.Headers.Contains("Content-Security-Policy").Should().BeFalse(
            "the strict policy is applied only outside Development, where the API serves only JSON");
    }
}
