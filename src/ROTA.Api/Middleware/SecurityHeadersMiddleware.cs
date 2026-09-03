namespace ROTA.Api.Middleware;

/// <summary>
/// Sets the response security headers the API was shipping without. Runs early so the headers land on
/// every response including the 500 produced by the global exception handler, the 429 from the rate
/// limiter, and the 403 from the ban gate — the paths a scanner reaches first.
///
/// HSTS is NOT set here; <c>UseHsts()</c> owns it and already runs outside Development.
///
/// The strict Content-Security-Policy is applied only outside Development, because Swagger UI is
/// registered in Development and its inline script and styles cannot load under it. Outside
/// Development this process serves nothing but JSON, so <c>default-src 'none'</c> is the honest
/// policy rather than a cautious one — there is no document, script, style or image to allow.
///
/// Nothing here is a substitute for CORS. CORS decides who may READ a cross-origin response; these
/// headers decide what a browser may DO with one it already has. The API needs both, and the WebGL
/// client is served from a different origin, so no header here may restrict cross-origin fetches —
/// that is CORS's job and it is configured separately with an explicit origin allowlist.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isDevelopment;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next          = next;
        _isDevelopment = env.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // OnStarting, not a write here: headers cannot be added once the response has begun, and the
        // downstream terminal middleware may start it. This runs at the last safe moment either way.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Stops a browser second-guessing Content-Type. A JSON endpoint that reflects any
            // user-controlled bytes is an XSS vector the moment something sniffs it as HTML.
            headers["X-Content-Type-Options"] = "nosniff";

            // Clickjacking. Nothing in this API is meant to be framed, by us or anyone.
            headers["X-Frame-Options"] = "DENY";

            // Full URLs can carry ids and tokens; do not hand them to third parties on navigation.
            headers["Referrer-Policy"] = "no-referrer";

            // Powerful features this API has no use for. Denying them costs nothing and removes them
            // from anything that manages to get a document rendered in this origin.
            headers["Permissions-Policy"] =
                "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), " +
                "microphone=(), payment=(), usb=()";

            if (!_isDevelopment)
            {
                // frame-ancestors is the modern half of X-Frame-Options; both are set because older
                // browsers honour only the latter. base-uri and form-action close the two injection
                // routes that survive default-src 'none'.
                headers["Content-Security-Policy"] =
                    "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
