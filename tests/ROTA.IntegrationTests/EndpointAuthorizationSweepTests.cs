using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
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

// Every route the API exposes is either protected or on a short, named list of routes that are
// meant to answer strangers. The sweep walks the live action table rather than a hand-kept list of
// controllers, so a controller added without [Authorize] fails here on its first build, and it then
// sends real requests, because an attribute that is present but not enforced — a policy name that
// no longer exists, middleware ordered wrongly — looks identical in the metadata.
//
// The three questions, each its own test: is every action marked; does the mark hold against no
// token at all; does an admin mark hold against a real, signed-in, ordinary player.
public class EndpointAuthorizationSweepTests : IAsyncLifetime
{
    private const string Password = "Test-Password-1!";

    // Routes that answer without a token, by design. Adding one here is a decision, not a fix.
    private static readonly HashSet<string> Anonymous = new(StringComparer.OrdinalIgnoreCase)
    {
        "api/auth/register",
        "api/auth/login",
        "api/auth/refresh",
        "api/auth/password-reset/request",
        "api/auth/password-reset/confirm",
        "api/legal/terms",
        "api/legal/privacy",
    };

    // Route prefixes that must carry an admin-grade policy, whatever the action.
    private static readonly (string Prefix, string[] Policies)[] Privileged =
    {
        ("api/admin",      new[] { "AdminOnly" }),
        ("api/dev",        new[] { "AdminOnly" }),
        ("api/moderation", new[] { "ModeratorOrAdmin", "AdminOnly" }),
    };

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _anon = null!;
    private HttpClient _plain = null!;

    private sealed record Route(string Template, string Method, ControllerActionDescriptor Action)
    {
        public override string ToString() => $"{Method} /{Template} ({Action.DisplayName})";
    }

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_authsweep_test")
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
                        // The sweep sends hundreds of requests from one address in seconds; a 429
                        // would read as "not 401" and fail the wrong test.
                        ["RateLimitConfig:AuthRequestsPerWindow"]   = "100000",
                        ["RateLimitConfig:PlayerRequestsPerWindow"] = "100000",
                    }));
            });

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RotaDbContext>().Database.MigrateAsync();

        _anon = _factory.CreateClient();
        await CreatePlayerAsync("sweep_plain");
        _plain = await SignInAsync("sweep_plain");
    }

    public async Task DisposeAsync()
    {
        _anon?.Dispose();
        _plain?.Dispose();
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

    private async Task CreatePlayerAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        await players.CreateAsync(Player.Create(username, $"{username}@rota.test",
            BCrypt.Net.BCrypt.HashPassword(Password, 12)));
    }

    private async Task<HttpClient> SignInAsync(string username)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = $"{username}@rota.test", Password = Password });
        res.StatusCode.Should().Be(HttpStatusCode.OK, "the sweep needs a real token for {0}", username);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    // ── the action table ─────────────────────────────────────────────────────

    private List<Route> Routes()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var routes = new List<Route>();
        foreach (var d in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            var template = d.AttributeRouteInfo?.Template;
            if (string.IsNullOrEmpty(template)) continue;   // conventional routing is not used here
            var methods = d.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods).Distinct().ToList() ?? new List<string>();
            if (methods.Count == 0) methods.Add("GET");
            foreach (var m in methods)
                routes.Add(new Route(template.TrimStart('/'), m, d));
        }
        routes.Count.Should().BeGreaterThan(100, "the sweep must see the whole action table, or it is testing nothing");
        return routes;
    }

    private static bool IsAnonymous(Route r)
        => r.Action.EndpointMetadata.OfType<IAllowAnonymous>().Any()
           || !r.Action.EndpointMetadata.OfType<IAuthorizeData>().Any();

    private static IEnumerable<string?> Policies(Route r)
        => r.Action.EndpointMetadata.OfType<IAuthorizeData>().Select(a => a.Policy);

    private static (string Prefix, string[] Policies)? PrivilegedPrefix(Route r)
    {
        foreach (var p in Privileged)
            if (r.Template.StartsWith(p.Prefix + "/", StringComparison.OrdinalIgnoreCase)
                || r.Template.Equals(p.Prefix, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }

    // A URL a router will match: every parameter filled with something of the right shape. The
    // value never has to be real — authorization answers before anything looks it up.
    private static string Fill(string template)
    {
        var sb = new StringBuilder("/");
        foreach (var seg in template.Split('/'))
        {
            if (sb.Length > 1) sb.Append('/');
            if (seg.StartsWith('{') && seg.EndsWith('}'))
            {
                var inner = seg[1..^1];
                var name = inner.Split(':')[0].TrimEnd('?');
                var constraint = inner.Contains(':') ? inner.Split(':')[1] : "";
                sb.Append(constraint.StartsWith("guid") || name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && !name.Contains("Definition")
                    ? Guid.NewGuid().ToString()
                    : constraint.StartsWith("int") ? "1" : "sweep");
            }
            else sb.Append(seg);
        }
        return sb.ToString();
    }

    private static HttpRequestMessage Request(Route r)
    {
        var req = new HttpRequestMessage(new HttpMethod(r.Method), Fill(r.Template));
        if (r.Method is "POST" or "PUT" or "PATCH")
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return req;
    }

    // ── 1. every action is marked ────────────────────────────────────────────

    [Fact]
    public void EveryAction_IsProtected_OrOnTheAnonymousList()
    {
        var routes = Routes();

        var unmarked = routes.Where(r => IsAnonymous(r) && !Anonymous.Contains(r.Template)).ToList();
        unmarked.Should().BeEmpty(
            "an action that answers without a token must be on the Anonymous list in this test, by decision");

        var stale = Anonymous.Where(a => !routes.Any(r => r.Template.Equals(a, StringComparison.OrdinalIgnoreCase))).ToList();
        stale.Should().BeEmpty("the Anonymous list names routes that no longer exist");

        var overmarked = routes.Where(r => !IsAnonymous(r) && Anonymous.Contains(r.Template)).ToList();
        overmarked.Should().BeEmpty("a route on the Anonymous list is protected after all — take it off the list");
    }

    [Fact]
    public void EveryPrivilegedRoute_CarriesItsPolicy()
    {
        var missing = Routes()
            .Where(r => PrivilegedPrefix(r) is { } p && !Policies(r).Any(pol => p.Policies.Contains(pol)))
            .ToList();
        missing.Should().BeEmpty("a route under an admin prefix must name an admin-grade policy");
    }

    // ── 2. the mark holds against no token ───────────────────────────────────

    [Fact]
    public async Task WithoutAToken_EveryProtectedRoute_Answers401()
    {
        var failures = new List<string>();
        foreach (var r in Routes().Where(r => !IsAnonymous(r)))
        {
            using var res = await _anon.SendAsync(Request(r));
            if (res.StatusCode != HttpStatusCode.Unauthorized)
                failures.Add($"{r} -> {(int)res.StatusCode}");
        }
        failures.Should().BeEmpty("authorization runs before binding, so a protected route answers a stranger with 401 and nothing else");
    }

    // ── 3. an admin mark holds against a real player ─────────────────────────

    [Fact]
    public async Task AsAnOrdinaryPlayer_EveryPrivilegedRoute_Answers403()
    {
        var failures = new List<string>();
        foreach (var r in Routes().Where(r => PrivilegedPrefix(r) is not null))
        {
            using var res = await _plain.SendAsync(Request(r));
            if (res.StatusCode != HttpStatusCode.Forbidden)
                failures.Add($"{r} -> {(int)res.StatusCode}");
        }
        failures.Should().BeEmpty("a signed-in player without the role is refused before the action runs");
    }

    // ── 4. what a stranger may read is exactly the anonymous list ────────────

    [Fact]
    public async Task TheAnonymousRoutes_DoNotDemandAToken()
    {
        var failures = new List<string>();
        foreach (var r in Routes().Where(r => Anonymous.Contains(r.Template)))
        {
            using var res = await _anon.SendAsync(Request(r));
            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                failures.Add($"{r} -> {(int)res.StatusCode}");
        }
        failures.Should().BeEmpty("these are the routes a stranger is meant to reach; a 400 is fine, a 401 is not");
    }
}
