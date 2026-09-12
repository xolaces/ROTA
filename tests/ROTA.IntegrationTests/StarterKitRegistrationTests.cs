using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Infrastructure.Persistence;
using ROTA.Shared.DTOs;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// A new account is dressed before its first screen: the Conscript set is granted and worn at
// registration (owner, 2026-09-12). Driven through the real register endpoint, because the grant
// rides the registration transaction and a unit test cannot see that.
public class StarterKitRegistrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_starterkit_test")
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
                        ["BetaGate:Enabled"]                    = "false",
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
    public async Task ANewAccount_WearsTheConscriptSet_FromItsFirstRequest()
    {
        var client = _factory.CreateClient();
        var terms = _factory.Services.GetRequiredService<IConfiguration>().GetValue("Legal:CurrentTermsVersion", 1);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = "kit_" + suffix,
            Email = $"kit_{suffix}@rota.test",
            Password = "Starter-Kit-1",
            AcceptedTermsVersion = terms,
        });
        reg.StatusCode.Should().Be(HttpStatusCode.Created, await reg.Content.ReadAsStringAsync());
        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var worn = await client.GetFromJsonAsync<List<EquippedItemResponse>>("/api/equipment");
        worn.Should().NotBeNull();
        worn!.Select(e => e.Slot).Should().BeEquivalentTo(
            new[] { "Head", "Neck", "Torso", "Gloves", "Ring1", "Ring2", "Boots", "Mount" });
        worn.Select(e => e.SetId).Distinct().Should().Equal("set_conscript");

        var owned = await client.GetFromJsonAsync<List<OwnedGearResponse>>("/api/equipment/owned");
        owned!.Should().HaveCount(8, "one of each starter piece, no spares");
        owned.Should().OnlyContain(g => g.OwnedQuantity == 1);
    }
}
