using FluentAssertions;
using Moq;
using ROTA.Infrastructure.Services;
using ROTA.Shared.DTOs;
using StackExchange.Redis;

namespace ROTA.UnitTests.Services;

/// <summary>
/// System 21 Slice 2 — RedisGuildChatStore unit tests. Drives the real production store against a
/// stateful in-memory fake IDatabase that implements only the three list ops the store uses
/// (LPUSH / LTRIM / LRANGE) with correct Redis semantics. Verifies the per-guild ring-buffer trims at
/// the cap, returns oldest→newest, and is isolated per guild (guild A's buffer never appears in B's).
/// Mirrors what a RedisWorldChatStore test would assert, scoped per guild.
/// </summary>
public class RedisGuildChatStoreTests
{
    // ── Stateful fake IDatabase: just enough list semantics for the store ───────
    private static (IConnectionMultiplexer mux, Dictionary<string, List<string>> lists) FakeRedis()
    {
        // Each key maps to a list stored newest-first (index 0 = head), mirroring LPUSH.
        var lists = new Dictionary<string, List<string>>();

        List<string> ListFor(RedisKey key)
        {
            var k = (string)key!;
            if (!lists.TryGetValue(k, out var l)) { l = new List<string>(); lists[k] = l; }
            return l;
        }

        var db = new Mock<IDatabase>();

        // LPUSH: insert at head (index 0).
        db.Setup(d => d.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, RedisValue value, When _, CommandFlags __) =>
            {
                var l = ListFor(key);
                l.Insert(0, (string)value!);
                return Task.FromResult((long)l.Count);
            });

        // LTRIM: keep only [start, stop] inclusive (supports the 0..cap-1 the store uses).
        db.Setup(d => d.ListTrimAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, long start, long stop, CommandFlags _) =>
            {
                var l = ListFor(key);
                int from = (int)start;
                int to = (int)Math.Min(stop, l.Count - 1);
                var kept = (from <= to && from < l.Count)
                    ? l.GetRange(from, to - from + 1)
                    : new List<string>();
                l.Clear();
                l.AddRange(kept);
                return Task.CompletedTask;
            });

        // LRANGE: return [start, stop] inclusive as RedisValue[].
        db.Setup(d => d.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, long start, long stop, CommandFlags _) =>
            {
                var l = ListFor(key);
                int from = (int)start;
                int to = (int)Math.Min(stop, l.Count - 1);
                var slice = (from <= to && from < l.Count)
                    ? l.GetRange(from, to - from + 1)
                    : new List<string>();
                return Task.FromResult(slice.Select(s => (RedisValue)s).ToArray());
            });

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return (mux.Object, lists);
    }

    private static ChatMessageDto Msg(string body) => new()
    {
        Id = Guid.NewGuid(),
        Scope = "Guild",
        SenderId = Guid.NewGuid(),
        SenderName = "Tester",
        SenderRole = "Player",
        Body = body,
        SentAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Append_ThenGetRecent_ReturnsMessages_OldestFirst()
    {
        var (mux, _) = FakeRedis();
        var store = new RedisGuildChatStore(mux);
        var guild = Guid.NewGuid();

        await store.AppendAsync(guild, Msg("first"));
        await store.AppendAsync(guild, Msg("second"));
        await store.AppendAsync(guild, Msg("third"));

        var recent = await store.GetRecentAsync(guild, 100);

        recent.Select(m => m.Body).Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public async Task Append_TrimsToCap_KeepingNewest100()
    {
        var (mux, _) = FakeRedis();
        var store = new RedisGuildChatStore(mux);
        var guild = Guid.NewGuid();

        // 150 messages > the 100 cap.
        for (int i = 0; i < 150; i++)
            await store.AppendAsync(guild, Msg($"m{i}"));

        var recent = await store.GetRecentAsync(guild, 100);

        recent.Should().HaveCount(100);
        // Newest 100 are m50..m149, returned oldest→newest.
        recent.First().Body.Should().Be("m50");
        recent.Last().Body.Should().Be("m149");
    }

    [Fact]
    public async Task Buffers_AreIsolatedPerGuild()
    {
        var (mux, _) = FakeRedis();
        var store = new RedisGuildChatStore(mux);
        var guildA = Guid.NewGuid();
        var guildB = Guid.NewGuid();

        await store.AppendAsync(guildA, Msg("a-only"));
        await store.AppendAsync(guildB, Msg("b-only"));

        var aMsgs = await store.GetRecentAsync(guildA, 100);
        var bMsgs = await store.GetRecentAsync(guildB, 100);

        aMsgs.Select(m => m.Body).Should().ContainSingle().Which.Should().Be("a-only");
        bMsgs.Select(m => m.Body).Should().ContainSingle().Which.Should().Be("b-only");
        aMsgs.Should().NotContain(m => m.Body == "b-only");
        bMsgs.Should().NotContain(m => m.Body == "a-only");
    }

    [Fact]
    public async Task GetRecent_EmptyGuild_ReturnsEmpty()
    {
        var (mux, _) = FakeRedis();
        var store = new RedisGuildChatStore(mux);

        var recent = await store.GetRecentAsync(Guid.NewGuid(), 100);

        recent.Should().BeEmpty();
    }
}
