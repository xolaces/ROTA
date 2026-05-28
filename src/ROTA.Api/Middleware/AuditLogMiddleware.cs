using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Api.Middleware;

// Runs after authentication so PlayerId is available from verified JWT claims.
// Never logs raw request body — stores SHA256 hash only.
public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditLogMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;

        if (method != HttpMethods.Post &&
            method != HttpMethods.Put &&
            method != HttpMethods.Delete)
        {
            await _next(context);
            return;
        }

        // Buffer the request body so it can be re-read by downstream handlers.
        context.Request.EnableBuffering();
        var inputHash = await HashRequestBodyAsync(context.Request);

        await _next(context);

        // After downstream runs: JWT claims are populated by authentication middleware.
        Guid? playerId = null;
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (Guid.TryParse(sub, out var pid))
            playerId = pid;

        var action = $"{method} {context.Request.Path}";
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var summary = $"HTTP {context.Response.StatusCode}";

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            await auditLog.AppendAsync(AuditLog.Create(playerId, action, inputHash, summary, ip));
        }
        catch
        {
            // Audit failure must never break the response pipeline.
        }
    }

    private static async Task<string?> HashRequestBodyAsync(HttpRequest request)
    {
        if ((request.ContentLength ?? 0) == 0)
            return null;

        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        request.Body.Position = 0; // rewind for downstream

        var hash = SHA256.HashData(ms.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // JwtRegisteredClaimNames is in System.IdentityModel.Tokens.Jwt — alias to avoid the using.
    private static class JwtRegisteredClaimNames
    {
        public const string Sub = "sub";
    }
}
