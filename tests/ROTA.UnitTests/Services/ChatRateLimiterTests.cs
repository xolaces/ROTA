using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Infrastructure.Services;
using StackExchange.Redis;

namespace ROTA.UnitTests.Services;

/// <summary>
/// Exploit-audit finding J — ChatRateLimiter unit tests. Drives the real limiter against a stateful
/// in-memory fake IDatabase implementing just INCR + EXPIRE. Verifies: sends are allowed up to the cap
/// then rejected; the counter is per-player; EXPIRE is armed on the first hit; and a Redis outage fails
/// OPEN (allow) so a cache blip never silences chat.
/// </summary>
public class ChatRateLimiterTests
{
    private static IConnectionMultiplexer FakeRedis(out Dictionary<string, long> counters,
                                                    out List<string> expiresArmed,
                                                    bool throwOnIncr = false)
    {
        var local = new Dictionary<string, long>();
        var armed = new List<string>();
        counters = local;
        expiresArmed = armed;

        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, long by, CommandFlags _) =>
            {
                if (throwOnIncr) throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down");
                var k = (string)key!;
                local.TryGetValue(k, out var v);
                v += by == 0 ? 1 : by;
                local[k] = v;
                return Task.FromResult(v);
            });
        // KeyExpireAsync(key, TimeSpan) resolves to the (RedisKey, TimeSpan?, ExpireWhen, CommandFlags)
        // overload in current StackExchange.Redis (the 3-arg one is deprecated) — match that one.
        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, TimeSpan? _, ExpireWhen __, CommandFlags ___) => { armed.Add((string)key!); return Task.FromResult(true); });

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return mux.Object;
    }

    private static IOptions<RateLimitConfig> Cfg(int max, int windowSeconds = 10)
        => Options.Create(new RateLimitConfig { ChatMessagesPerWindow = max, ChatWindowSeconds = windowSeconds });

    [Fact]
    public async Task AllowsUpToCap_ThenRejects()
    {
        var mux = FakeRedis(out _, out _);
        var limiter = new ChatRateLimiter(mux, Cfg(max: 3));
        var player = Guid.NewGuid();

        (await limiter.TryConsumeAsync(player)).Should().BeTrue();
        (await limiter.TryConsumeAsync(player)).Should().BeTrue();
        (await limiter.TryConsumeAsync(player)).Should().BeTrue();
        // 4th in the window is over the cap.
        (await limiter.TryConsumeAsync(player)).Should().BeFalse();
    }

    [Fact]
    public async Task ArmsExpiry_OnFirstHitOnly()
    {
        var mux = FakeRedis(out _, out var armed);
        var limiter = new ChatRateLimiter(mux, Cfg(max: 5));
        var player = Guid.NewGuid();

        await limiter.TryConsumeAsync(player);
        await limiter.TryConsumeAsync(player);

        armed.Should().ContainSingle("the TTL is set only when the counter is first created");
        armed[0].Should().Be($"ratelimit:chat:player:{player}");
    }

    [Fact]
    public async Task CountersAreIsolatedPerPlayer()
    {
        var mux = FakeRedis(out _, out _);
        var limiter = new ChatRateLimiter(mux, Cfg(max: 1));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        (await limiter.TryConsumeAsync(a)).Should().BeTrue();
        (await limiter.TryConsumeAsync(a)).Should().BeFalse("a is now over its own cap");
        (await limiter.TryConsumeAsync(b)).Should().BeTrue("b has its own counter");
    }

    [Fact]
    public async Task FailsOpen_OnRedisOutage()
    {
        var mux = FakeRedis(out _, out _, throwOnIncr: true);
        var limiter = new ChatRateLimiter(mux, Cfg(max: 1));

        // Redis is unreachable — the throttle must degrade to "allowed", never throw out of the hub.
        (await limiter.TryConsumeAsync(Guid.NewGuid())).Should().BeTrue();
    }
}
