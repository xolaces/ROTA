using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Infrastructure.Persistence;
using ROTA.Shared.DTOs;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// Quest pacing and the chat hub, through the real pipeline against real Postgres and Redis. Unit tests
// hand QuestService a mock pacer and call ChatHub methods directly, so neither the DI wiring (the pacer
// is an optional constructor argument: if it stops resolving, pacing silently turns off) nor the hub's
// route, auth and transport are exercised anywhere else.
//
// HOW TO VERIFY THIS TEST CATCHES THE BUG:
//   - Remove the IActionPacer registration from AddRotaServices: the pacing test finds no 429.
public class QuestPacingAndChatHubTests : IAsyncLifetime
{
    private const string Quest = "q001";
    private static readonly TimeSpan PastTheWindow = TimeSpan.FromMilliseconds(950); // shipped window: 800 ms
    private static readonly TimeSpan HubTimeout = TimeSpan.FromSeconds(15);

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private readonly List<HubConnection> _connections = new();

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_pacing_hub_test")
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
        foreach (var c in _connections) await c.DisposeAsync();
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

    private async Task<(HttpClient Client, AuthResponse Auth)> RegisterAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var terms = _factory.Services.GetRequiredService<IConfiguration>().GetValue("Legal:CurrentTermsVersion", 1);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = prefix + "_" + suffix,
            Email = $"{prefix}_{suffix}@rota.test",
            Password = "Pacing-Hub-1",
            AcceptedTermsVersion = terms,
        });
        reg.StatusCode.Should().Be(HttpStatusCode.Created, await reg.Content.ReadAsStringAsync());
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    private static Task<HttpResponseMessage> AttemptAsync(HttpClient client)
        => client.PostAsJsonAsync($"/api/quests/{Quest}/attempt", new { difficulty = "Normal" });

    // The token goes on the query string: browsers cannot set a header on a WebSocket, so this is the
    // path the WebGL client depends on.
    private HubConnection Connect(string? token)
    {
        var url = "http://localhost/hubs/chat" + (token is null ? "" : "?access_token=" + token);
        var connection = new HubConnectionBuilder()
            .WithUrl(url, o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        _connections.Add(connection);
        return connection;
    }

    [Fact]
    public async Task TwoAttemptsAtOnce_OneIsTurnedAway_AndTheWindowCloses()
    {
        var (client, _) = await RegisterAsync("pace");

        // A cold first request can outlast the window on its own, so it does not count.
        var warmup = await AttemptAsync(client);
        warmup.StatusCode.Should().Be(HttpStatusCode.OK, await warmup.Content.ReadAsStringAsync());
        await Task.Delay(PastTheWindow);

        var pair = await Task.WhenAll(AttemptAsync(client), AttemptAsync(client));

        pair.Select(r => r.StatusCode).Should().BeEquivalentTo(
            new[] { HttpStatusCode.OK, HttpStatusCode.TooManyRequests });

        var early = pair.Single(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        early.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(1);
        var body = (await early.Content.ReadFromJsonAsync<QuestResultResponse>())!;
        body.FailureCode.Should().Be(QuestFailureCode.TooFast);
        body.RetryAfterMs.Should().BeInRange(1, 800);

        await Task.Delay(PastTheWindow);
        var after = await AttemptAsync(client);
        after.StatusCode.Should().Be(HttpStatusCode.OK, await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnePlayersWindow_DoesNotHoldUpAnother()
    {
        var (first, _) = await RegisterAsync("pace_a");
        var (second, _) = await RegisterAsync("pace_b");

        var results = await Task.WhenAll(AttemptAsync(first), AttemptAsync(second));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hub_RefusesAConnectionWithNoToken()
    {
        var connection = Connect(token: null);

        var connect = () => connection.StartAsync();

        (await connect.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WorldMessage_ReachesOtherPlayers_Cleaned_AndIsStored()
    {
        var (_, sender) = await RegisterAsync("hub_a");
        var (_, listener) = await RegisterAsync("hub_b");

        var received = new TaskCompletionSource<ChatMessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listening = Connect(listener.AccessToken);
        listening.On<ChatMessageDto>("WorldMessage", m => received.TrySetResult(m));
        await listening.StartAsync();

        var sending = Connect(sender.AccessToken);
        await sending.StartAsync();
        await sending.InvokeAsync("SendWorldMessage", "he​llo‮ world");

        var message = await received.Task.WaitAsync(HubTimeout);
        message.Scope.Should().Be("World");
        message.Body.Should().Be("hello world", "zero-width and bidi characters are stripped");
        message.SenderRole.Should().Be("Player");

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IWorldChatStore>().GetRecentAsync(10);
        stored.Should().ContainSingle(m => m.Id == message.Id, "world chat lands in the Redis ring buffer");
    }

    [Fact]
    public async Task GuildMessage_FromAPlayerWithNoGuild_IsRefused()
    {
        var (_, auth) = await RegisterAsync("hub_c");

        var refused = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = Connect(auth.AccessToken);
        connection.On<string>("GuildChatUnavailable", m => refused.TrySetResult(m));
        await connection.StartAsync();

        await connection.InvokeAsync("SendGuildMessage", "anyone there?");

        (await refused.Task.WaitAsync(HubTimeout)).Should().Contain("not in a guild");
    }
}
