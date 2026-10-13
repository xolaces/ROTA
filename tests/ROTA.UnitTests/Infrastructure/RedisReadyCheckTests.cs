using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using ROTA.Api.Health;
using StackExchange.Redis;

namespace ROTA.UnitTests.Infrastructure;

// /health/ready must say Unhealthy when Redis is down; /health used to say 200 regardless.
public class RedisReadyCheckTests
{
    private static RedisReadyCheck Check(Mock<IDatabase> db)
    {
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return new RedisReadyCheck(mux.Object);
    }

    [Fact]
    public async Task Healthy_when_redis_answers()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(TimeSpan.FromMilliseconds(1));

        var result = await Check(db).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Unhealthy_when_redis_is_down()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
          .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var result = await Check(db).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
