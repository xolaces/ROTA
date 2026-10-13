using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ROTA.Infrastructure.Persistence;
using StackExchange.Redis;

namespace ROTA.Api.Health;

// /health/ready runs these; /health runs none. A liveness probe that touched the database would
// restart a healthy API container every time Postgres blinked, so the two stay separate: an uptime
// monitor watches /health/ready, the container healthcheck watches /health.

/// <summary>Postgres answers a round trip.</summary>
public sealed class PostgresReadyCheck : IHealthCheck
{
    private readonly RotaDbContext _db;
    public PostgresReadyCheck(RotaDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("postgres unreachable");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("postgres unreachable", e);
        }
    }
}

/// <summary>Redis answers a PING.</summary>
public sealed class RedisReadyCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _mux;
    public RedisReadyCheck(IConnectionMultiplexer mux) => _mux = mux;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await _mux.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("redis unreachable", e);
        }
    }
}
