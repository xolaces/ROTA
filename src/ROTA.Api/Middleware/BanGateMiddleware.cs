using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ROTA.Application.Interfaces;

namespace ROTA.Api.Middleware;

// Global ban gate (audit fix). Ban revokes refresh tokens, but the stateless 15-minute access JWT
// outlives the revocation — without a per-request check a banned player keeps hitting raids,
// claiming loot, allocating stats, and spending gems until the token expires. Individual services
// re-checked IsBanned inconsistently (Quest/Gauntlet/Social/Chat yes; Raid/Stat/Gem/Item/Guild no);
// a single authoritative gate here can't be forgotten on new endpoints.
//
// Scope: mutating methods only (POST/PUT/DELETE/PATCH) — exactly the state changes a ban must stop.
// Reads cost no DB hit and go stale-harmless when the JWT dies. Runs after authentication (verified
// sub claim) and after AuditLogMiddleware (so the blocked 403 is still audited).
public class BanGateMiddleware
{
    private readonly RequestDelegate _next;

    public BanGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IPlayerRepository players)
    {
        var method = context.Request.Method;
        bool mutating = method == HttpMethods.Post
                     || method == HttpMethods.Put
                     || method == HttpMethods.Delete
                     || method == HttpMethods.Patch;

        if (!mutating)
        {
            await _next(context);
            return;
        }

        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(sub, out var playerId))
        {
            // Unauthenticated (e.g. login/register) — nothing to gate.
            await _next(context);
            return;
        }

        var player = await players.FindByIdAsync(playerId, context.RequestAborted);
        if (player is not null && player.IsBanned)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { message = "Account banned." }, context.RequestAborted);
            return;
        }

        await _next(context);
    }
}
