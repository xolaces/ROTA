namespace ROTA.Api.Middleware;

// Logs method, path, status code, and duration.
// SECURITY: never logs Authorization headers, passwords, or JWT payloads.
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var start = DateTime.UtcNow;
        await _next(context);
        var duration = DateTime.UtcNow - start;

        _logger.LogInformation("{Method} {Path} {StatusCode} {DurationMs}ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            duration.TotalMilliseconds);
    }
}