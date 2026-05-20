namespace ROTA.Api.Middleware;

// BETA-PLACEHOLDER: Redis-backed per-player rate limiting implemented in Week 2.
// Stub passes all requests through for now.
// Full implementation: per-player limits on game actions, per-IP limits on auth endpoints.
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;

    public RateLimitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // TODO-PHASE0-WEEK2: implement Redis-backed per-player rate limiting
        await _next(context);
    }
}