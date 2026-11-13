using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;

namespace ROTA.Api.Middleware;

/// <summary>
/// While the owner has maintenance switched on (admin CLI <c>maintenance on</c>), every game request
/// is answered 503 with the owner's line, so players see "back soon" instead of half-working screens
/// during a deploy. Runs after authentication so an Admin token passes and the owner can check the
/// new build before reopening.
///
/// Always open: health checks, /api/status (the client polls it to know when to come back), the auth
/// endpoints (an admin has to be able to log in) and the legal texts.
/// </summary>
public sealed class MaintenanceMiddleware
{
    private static readonly string[] AlwaysOpen = { "/health", "/api/status", "/api/auth", "/api/legal", "/error" };

    private readonly RequestDelegate _next;

    public MaintenanceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IServiceNoticeStore notices)
    {
        var notice = await notices.GetAsync(context.RequestAborted);
        if (!notice.Maintenance || IsOpen(context.Request.Path) || context.User.IsInRole(nameof(PlayerRoles.Admin)))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "30";
        await context.Response.WriteAsJsonAsync(
            new { maintenance = true, message = notice.MaintenanceMessage }, context.RequestAborted);
    }

    private static bool IsOpen(PathString path)
    {
        foreach (var prefix in AlwaysOpen)
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
