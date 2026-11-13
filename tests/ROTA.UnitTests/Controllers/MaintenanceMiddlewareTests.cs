using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ROTA.Api.Middleware;
using ROTA.Application.Interfaces;

namespace ROTA.UnitTests.Controllers;

/// <summary>
/// Patch 0.5.0 — maintenance mode. While it is on, game requests get 503 with the owner's line;
/// admins, health checks, the status endpoint and login stay open.
/// </summary>
public class MaintenanceMiddlewareTests
{
    private sealed class FakeNotices : IServiceNoticeStore
    {
        public ServiceNotice Notice = ServiceNotice.None;
        public Task<ServiceNotice> GetAsync(CancellationToken ct = default) => Task.FromResult(Notice);
        public Task SetMaintenanceAsync(bool on, string? message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetMotdAsync(string? text, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<(int status, bool reachedApp, string body)> Run(
        string path, ServiceNotice notice, bool admin = false)
    {
        bool reached = false;
        var middleware = new MaintenanceMiddleware(_ => { reached = true; return Task.CompletedTask; });
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (admin)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Role, "Admin") }, authenticationType: "test"));

        await middleware.InvokeAsync(ctx, new FakeNotices { Notice = notice });

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        return (ctx.Response.StatusCode, reached, body);
    }

    private static readonly ServiceNotice Closed = new(true, "Back at 9.", null);

    [Fact]
    public async Task Open_EveryRequestPasses()
    {
        var (status, reached, _) = await Run("/api/raids", ServiceNotice.None);
        reached.Should().BeTrue();
        status.Should().Be(200);
    }

    [Fact]
    public async Task Closed_AGameRequestGets503_WithTheOwnersLine_AndNeverReachesTheApp()
    {
        var (status, reached, body) = await Run("/api/raids/123/hit", Closed);

        reached.Should().BeFalse();
        status.Should().Be(503);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("maintenance").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("message").GetString().Should().Be("Back at 9.");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/api/status")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/legal/terms")]
    public async Task Closed_TheAlwaysOpenPathsStillPass(string path)
    {
        var (_, reached, _) = await Run(path, Closed);
        reached.Should().BeTrue($"{path} must stay reachable during maintenance");
    }

    [Theory]
    [InlineData("/api/authx")]        // a prefix match must be on whole segments
    [InlineData("/hubs/chat")]
    [InlineData("/api/players/me")]
    public async Task Closed_EverythingElseIsShut(string path)
    {
        var (status, reached, _) = await Run(path, Closed);
        reached.Should().BeFalse();
        status.Should().Be(503);
    }

    [Fact]
    public async Task Closed_AnAdminStillGetsIn_SoTheOwnerCanCheckTheBuild()
    {
        var (_, reached, _) = await Run("/api/raids", Closed, admin: true);
        reached.Should().BeTrue();
    }
}
