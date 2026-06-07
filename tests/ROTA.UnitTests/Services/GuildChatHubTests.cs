using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using ROTA.Api.SignalR;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Services;
using ROTA.Shared.DTOs;
using StackExchange.Redis;

namespace ROTA.UnitTests.Services;

/// <summary>
/// System 21 Slice 2 — ChatHub guild-chat gate tests. Drives the real ChatHub with mocked SignalR
/// plumbing (Context/Clients/Groups) and the real RedisGuildChatStore over an in-memory fake IDatabase,
/// so we assert both the gate decision AND the buffer side-effect:
///   • a guild member can send → broadcast to the guild group + appended to that guild's buffer,
///   • a non-member (no GuildId) is rejected on send and on join (member-gate),
///   • a muted member is rejected (mute-gate, mirroring world/raid chat exactly).
/// </summary>
public class GuildChatHubTests
{
    // ── Stateful fake IDatabase (LPUSH/LTRIM/LRANGE) so the real store works without a container ──
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

    private sealed class Harness
    {
        public ChatHub Hub = null!;
        public RedisGuildChatStore Store = null!;
        public Mock<IPlayerRepository> Players = new();
        public Mock<IClientProxy> GroupProxy = new();
        public Mock<ISingleClientProxy> CallerProxy = new();
        public Mock<IGroupManager> Groups = new();
        public string? LastCallerEvent;
        public string? LastGroupName;

        public Harness(Player caller)
        {
            Store = new RedisGuildChatStore(FakeRedis());
            Players.Setup(p => p.FindByIdAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

            var clients = new Mock<IHubCallerClients>();
            clients.Setup(c => c.Caller).Returns(CallerProxy.Object);
            clients.Setup(c => c.Group(It.IsAny<string>()))
                .Callback((string g) => LastGroupName = g)
                .Returns(GroupProxy.Object);

            // Capture which event the caller was sent (Muted / GuildChatUnavailable).
            CallerProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Callback((string method, object?[] _, CancellationToken __) => LastCallerEvent = method)
                .Returns(Task.CompletedTask);
            GroupProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var ctx = new Mock<HubCallerContext>();
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", caller.Id.ToString()),
                new Claim("display_name", caller.DisplayName),
            });
            ctx.Setup(c => c.User).Returns(new ClaimsPrincipal(identity));
            ctx.Setup(c => c.ConnectionId).Returns("conn-1");

            Hub = new ChatHub(Mock.Of<IWorldChatStore>(), Store, Players.Object)
            {
                Clients = clients.Object,
                Groups = Groups.Object,
                Context = ctx.Object,
            };
        }
    }

    private static Player MakePlayer(string username)
    {
        var p = Player.Create(username, $"{username}@rota.test", "hash");
        p.UpdateDisplayName(username);
        return p;
    }

    [Fact]
    public async Task SendGuildMessage_Member_BroadcastsAndAppends()
    {
        var guildId = Guid.NewGuid();
        var member = MakePlayer("alice");
        member.JoinGuild(guildId, ROTA.Domain.Enums.GuildRank.Member);
        var h = new Harness(member);

        await h.Hub.SendGuildMessage("hello guild");

        // Broadcast to the per-guild group.
        h.LastGroupName.Should().Be($"guild:{guildId}");
        h.GroupProxy.Verify(p => p.SendCoreAsync("GuildMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        // Landed in that guild's ring buffer.
        var history = await h.Store.GetRecentAsync(guildId, 100);
        history.Should().ContainSingle().Which.Body.Should().Be("hello guild");
        h.LastCallerEvent.Should().BeNull("a member is not blocked");
    }

    [Fact]
    public async Task SendGuildMessage_NonMember_Rejected_NoAppend()
    {
        var nonMember = MakePlayer("bob"); // GuildId stays null
        var h = new Harness(nonMember);

        await h.Hub.SendGuildMessage("can I talk?");

        h.LastCallerEvent.Should().Be("GuildChatUnavailable");
        h.GroupProxy.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendGuildMessage_MutedMember_Rejected_NoAppend()
    {
        var guildId = Guid.NewGuid();
        var muted = MakePlayer("carol");
        muted.JoinGuild(guildId, ROTA.Domain.Enums.GuildRank.Member);
        muted.Mute(DateTimeOffset.UtcNow.AddMinutes(30));
        var h = new Harness(muted);

        await h.Hub.SendGuildMessage("spam spam spam");

        h.LastCallerEvent.Should().Be("Muted");
        h.GroupProxy.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
        var history = await h.Store.GetRecentAsync(guildId, 100);
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task SendGuildMessage_EmptyBody_NoOp()
    {
        var guildId = Guid.NewGuid();
        var member = MakePlayer("dan");
        member.JoinGuild(guildId, ROTA.Domain.Enums.GuildRank.Member);
        var h = new Harness(member);

        await h.Hub.SendGuildMessage("   ");

        h.GroupProxy.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
        (await h.Store.GetRecentAsync(guildId, 100)).Should().BeEmpty();
    }

    [Fact]
    public async Task JoinGuildChannel_Member_AddsToGroup()
    {
        var guildId = Guid.NewGuid();
        var member = MakePlayer("erin");
        member.JoinGuild(guildId, ROTA.Domain.Enums.GuildRank.Officer);
        var h = new Harness(member);

        await h.Hub.JoinGuildChannel();

        h.Groups.Verify(g => g.AddToGroupAsync("conn-1", $"guild:{guildId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinGuildChannel_NonMember_Rejected_NoGroup()
    {
        var nonMember = MakePlayer("frank");
        var h = new Harness(nonMember);

        await h.Hub.JoinGuildChannel();

        h.Groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.LastCallerEvent.Should().Be("GuildChatUnavailable");
    }
}
