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
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// The FIRST tests in this suite to drive a controller over HTTP.
//
// Everything else here calls services and repositories directly, which leaves a real gap: routing,
// the authorization policy, model binding, validator wiring and JSON serialization are all untested.
// That gap matters most for what changed on 2026-08-26 --
// POST /api/moderation/players/{id}/unmute gained a REQUIRED request body (a breaking change), and
// GET .../history is new. A service-level test cannot tell you that an endpoint is reachable, that
// its policy actually bites, or that a missing body yields 400 rather than a null-reference 500.
//
// Auth is real end to end: players are created through the repository (registration is beta-key
// gated, which is a different flow and not what is under test) and then LOG IN through
// POST /api/auth/login to obtain genuine RS256 tokens.
//
// One structural note: all three logins happen in InitializeAsync. xUnit builds a fresh instance --
// and therefore fresh containers -- per test method, so the auth rate limit (10 per 60s per IP)
// resets each time and three logins sit comfortably inside it. Keep it that way: each login is a
// BCrypt-12 verification, and a fixture that logs in on demand would both slow every test and walk
// toward that ceiling for no benefit.
public class ModerationEndpointTests : IAsyncLifetime
{
    private const string Password = "Test-Password-1!";

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    private HttpClient _admin     = null!;
    private HttpClient _moderator = null!;
    private HttpClient _plain     = null!;

    private Guid _adminId, _moderatorId;
    private string _targetName = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_moderation_api_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Keys come from TestJwtKeys, which publishes them as environment variables at assembly load.
        // Setting them through AddInMemoryCollection below would NOT work: Program.cs reads the public
        // key eagerly while the builder is constructed, before these callbacks run, so the token would
        // be signed with one key and validated against another. See TestJwtKeys for the full story.
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

        _adminId     = await CreatePlayerAsync("apitest_admin", PlayerRoles.Admin);
        _moderatorId = await CreatePlayerAsync("apitest_mod",   PlayerRoles.Moderator);
        await CreatePlayerAsync("apitest_plain", PlayerRoles.None);

        _targetName = "apitest_target";
        await CreatePlayerAsync(_targetName, PlayerRoles.None);

        _admin     = await SignInAsync("apitest_admin");
        _moderator = await SignInAsync("apitest_mod");
        _plain     = await SignInAsync("apitest_plain");
    }

    public async Task DisposeAsync()
    {
        _admin?.Dispose();
        _moderator?.Dispose();
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

    private async Task<Guid> CreatePlayerAsync(string username, PlayerRoles roles)
    {
        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();

        var player = Player.Create(username, $"{username}@rota.test",
            BCrypt.Net.BCrypt.HashPassword(Password, 12));
        if (roles.HasFlag(PlayerRoles.Admin))     player.GrantRole(PlayerRoles.Admin);
        if (roles.HasFlag(PlayerRoles.Moderator)) player.GrantRole(PlayerRoles.Moderator);

        await players.CreateAsync(player);
        return player.Id;
    }

    /// <summary>Logs in for real and returns a client carrying the resulting bearer token.</summary>
    private async Task<HttpClient> SignInAsync(string username)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = $"{username}@rota.test", Password = Password });

        res.StatusCode.Should().Be(HttpStatusCode.OK,
            "the test fixture cannot proceed without a real token for {0}", username);

        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    private Task<HttpResponseMessage> MuteAsync(HttpClient who, string target, string reason, int minutes = 60)
        => who.PostAsJsonAsync($"/api/moderation/players/{target}/mute",
            new MutePlayerRequest { DurationMinutes = minutes, Reason = reason });

    private Task<HttpResponseMessage> UnmuteAsync(HttpClient who, string target, string reason)
        => who.PostAsJsonAsync($"/api/moderation/players/{target}/unmute",
            new UnmutePlayerRequest { Reason = reason });

    // ---- the authorization policy actually bites ----------------------------------------------

    [Fact]
    public async Task History_IsRefusedToAnOrdinaryPlayer()
    {
        var res = await _plain.GetAsync($"/api/moderation/players/{_targetName}/history");

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the new endpoint inherits [Authorize(Policy = \"ModeratorOrAdmin\")] -- a service-level "
            + "test cannot prove the policy is wired to the route");
    }

    [Fact]
    public async Task History_IsRefusedWithNoTokenAtAll()
    {
        using var anonymous = _factory.CreateClient();

        var res = await anonymous.GetAsync($"/api/moderation/players/{_targetName}/history");

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task History_UnknownPlayer_Is404()
    {
        var res = await _admin.GetAsync("/api/moderation/players/no_such_player/history");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an empty history and an absent player are different answers");
    }

    // ---- the breaking change: unmute now requires a body ---------------------------------------

    [Fact]
    public async Task Unmute_WithNoReason_Is400_NotAServerError()
    {
        var target = "apitest_noreason";
        await CreatePlayerAsync(target, PlayerRoles.None);
        (await MuteAsync(_admin, target, "Spam.")).StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await UnmuteAsync(_admin, target, "   ");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the validator must reject it -- a reasonless reversal is exactly what §6 forbids");
    }

    [Fact]
    public async Task Unmute_WithNoBodyAtAll_Is400_NotANullReference()
    {
        var target = "apitest_nobody";
        await CreatePlayerAsync(target, PlayerRoles.None);
        await MuteAsync(_admin, target, "Spam.");

        // A client that has not been updated for the new contract sends the old bodyless request.
        // ASP.NET answers 415 (there is no Content-Type to bind from) rather than 400 — either is a
        // clean refusal; what matters is that it is never a 500 from binding null into the validator.
        var bodyless = await _admin.PostAsync($"/api/moderation/players/{target}/unmute", content: null);

        bodyless.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType,
            "a bodyless POST has no Content-Type to bind from");
        ((int)bodyless.StatusCode).Should().BeLessThan(500,
            "a stale client must get a refusal, not a server error");

        // The nearer miss: a client that sends the right content type but an empty object. That DOES
        // reach the validator, and must come back 400 rather than unmuting with a blank reason.
        var emptyJson = await _admin.PostAsync($"/api/moderation/players/{target}/unmute",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        emptyJson.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an absent reason is a reasonless reversal, which §6 forbids");

        // And neither attempt lifted the mute.
        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        (await players.FindByUsernameAsync(target))!.IsMuted.Should().BeTrue();
    }

    // ---- the §6 authority gate, end to end -----------------------------------------------------

    [Fact]
    public async Task Unmute_ByModerator_OnAnAdminPlacedMute_IsRefused()
    {
        var target = "apitest_adminmuted";
        await CreatePlayerAsync(target, PlayerRoles.None);

        (await MuteAsync(_admin, target, "Harassment.")).StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await UnmuteAsync(_moderator, target, "They apologised.");

        ((int)res.StatusCode).Should().BeGreaterThanOrEqualTo(400,
            "a moderator must not silently override an admin's decision");

        // And the mute genuinely still stands, not merely a refused response.
        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var after = await players.FindByUsernameAsync(target);
        after!.IsMuted.Should().BeTrue();
    }

    [Fact]
    public async Task Unmute_ByModerator_OnAModeratorPlacedMute_Succeeds()
    {
        var target = "apitest_modmuted";
        await CreatePlayerAsync(target, PlayerRoles.None);

        (await MuteAsync(_moderator, target, "Spam.")).StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await UnmuteAsync(_moderator, target, "Warned instead.");

        res.StatusCode.Should().Be(HttpStatusCode.OK,
            "the gate is about AUTHORITY, not about reversals in general");

        using var scope = _factory.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        (await players.FindByUsernameAsync(target))!.IsMuted.Should().BeFalse();
    }

    // ---- the read side actually returns the record ---------------------------------------------

    [Fact]
    public async Task History_ReturnsTheGovernanceRecord_NewestFirst_WithRolesAndReasons()
    {
        var target = "apitest_history";
        await CreatePlayerAsync(target, PlayerRoles.None);

        await MuteAsync(_moderator, target, "First offence.");
        await UnmuteAsync(_moderator, target, "Warned instead.");
        await MuteAsync(_admin, target, "Second offence.");

        var res = await _admin.GetAsync($"/api/moderation/players/{target}/history");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await res.Content.ReadFromJsonAsync<List<PunishmentLogEntryResponse>>();

        payload.Should().NotBeNull();
        var history = payload!;
        history.Should().HaveCount(3);

        // Newest first.
        history[0].Type.Should().Be("Mute");
        history[0].Reason.Should().Be("Second offence.");
        history[0].ActorRole.Should().Be("Admin");
        history[0].ExpiresAt.Should().NotBeNull();

        history[1].Type.Should().Be("Unmute");
        history[1].ActorRole.Should().Be("Moderator");
        history[1].ReversalOfId.Should().Be(history[2].Id, "a reversal points at what it lifted");

        history[2].Type.Should().Be("Mute");
        history[2].Reason.Should().Be("First offence.");

        // The serialized shape is the contract the ops dashboard will read.
        history[0].TargetUsername.Should().Be(target);
        history[0].ActorPlayerId.Should().Be(_adminId);
        history[1].ActorPlayerId.Should().Be(_moderatorId);
    }

    [Fact]
    public async Task History_IsEmptyForACleanPlayer_NotAnError()
    {
        var target = "apitest_clean";
        await CreatePlayerAsync(target, PlayerRoles.None);

        var res = await _admin.GetAsync($"/api/moderation/players/{target}/history");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<List<PunishmentLogEntryResponse>>())
            .Should().BeEmpty();
    }
}
