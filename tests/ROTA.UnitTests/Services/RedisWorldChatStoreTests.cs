using FluentAssertions;
using Moq;
using ROTA.Infrastructure.Services;
using ROTA.Shared.DTOs;
using StackExchange.Redis;

namespace ROTA.UnitTests.Services;

/// <summary>
/// World-chat ring-buffer tests (T36). Drives the real RedisWorldChatStore against a stateful in-memory
/// fake IDatabase (LPUSH / LTRIM / LRANGE). The cap-trim assertion here is the PARITY BASELINE for
/// exploit-audit finding M: RedisGuildChatStore must evict identically (see
/// <see cref="RedisGuildChatStoreTests.Append_TrimsToCap_KeepingNewest100"/>), so a future regression in
/// either store's LTRIM is caught.
/// </summary>
public class RedisWorldChatStoreTests
{
    private static IConnectionMultiplexer FakeRedis()
    {
        var lists = new Dictionary<string, List<string>>();
        List<string> ListFor(RedisKey key)
        {
            var k = (string)key!;
            if (!lists.TryGetValue(k, out var l)) { l = new List<string>(); lists[k] = l; }
            return l;
        }

        var db = new Mock<IDatabase>();
        db.Setup(d => d.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, RedisValue value, When _, CommandFlags __) => { var l = ListFor(key); l.Insert(0, (string)value!); return Task.FromResult((long)l.Count); });
        db.Setup(d => d.ListTrimAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, long start, long stop, CommandFlags _) =>
            {
                var l = ListFor(key); int from = (int)start; int to = (int)Math.Min(stop, l.Count - 1);
                var kept = (from <= to && from < l.Count) ? l.GetRange(from, to - from + 1) : new List<string>();
                l.Clear(); l.AddRange(kept); return Task.CompletedTask;
            });
        db.Setup(d => d.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, long start, long stop, CommandFlags _) =>
            {
                var l = ListFor(key); int from = (int)start; int to = (int)Math.Min(stop, l.Count - 1);
                var slice = (from <= to && from < l.Count) ? l.GetRange(from, to - from + 1) : new List<string>();
                return Task.FromResult(slice.Select(s => (RedisValue)s).ToArray());
            });

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return mux.Object;
    }

    private static ChatMessageDto Msg(string body) => new()
    {
        Id = Guid.NewGuid(),
        Scope = "World",
        SenderId = Guid.NewGuid(),
        SenderName = "Tester",
        SenderUsername = "tester",
        SenderRole = "Player",
        Body = body,
        SentAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Append_ThenGetRecent_ReturnsMessages_OldestFirst()
    {
        var store = new RedisWorldChatStore(FakeRedis());

        await store.AppendAsync(Msg("first"));
        await store.AppendAsync(Msg("second"));
        await store.AppendAsync(Msg("third"));

        var recent = await store.GetRecentAsync(100);

        recent.Select(m => m.Body).Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public async Task Append_TrimsToCap_KeepingNewest100()
    {
        var store = new RedisWorldChatStore(FakeRedis());

        for (int i = 0; i < 150; i++)
            await store.AppendAsync(Msg($"m{i}"));

        var recent = await store.GetRecentAsync(100);

        recent.Should().HaveCount(100);
        recent.First().Body.Should().Be("m50");
        recent.Last().Body.Should().Be("m149");
    }
}
