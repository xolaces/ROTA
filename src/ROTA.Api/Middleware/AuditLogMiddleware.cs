namespace ROTA.Api.Middleware;

// BETA-PLACEHOLDER: full audit log writing implemented in Week 2 alongside auth endpoints.
// Stub passes all requests through for now.
// Full implementation: writes to audit_log table on every state-changing request.
// Runs after authentication so PlayerId is available from JWT claims.
public class AuditLogMiddleware
{
	private readonly RequestDelegate _next;

	public AuditLogMiddleware(RequestDelegate next)
	{
		_next = next;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		// TODO-PHASE0-WEEK2: write audit log entry for all state-changing requests
		await _next(context);
	}
}