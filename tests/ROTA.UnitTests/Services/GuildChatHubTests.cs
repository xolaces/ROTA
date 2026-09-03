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
        public Mock<IChatRateLimiter> RateLimiter = new();
        public Mock<IClientProxy> GroupProxy = new();
        public Mock<ISingleClientProxy> CallerProxy = new();
        public Mock<IGroupManager> Groups = new();
        public Mock<IGuildMembershipRepository> GuildMembers = new();
        public string? LastCallerEvent;
        public string? LastGroupName;
        // Who the message was actually addressed to. Guild chat fans out to the guild's CURRENT
        // members rather than to a join-time SignalR group, so this is the delivery list under test.
        public IReadOnlyList<string>? LastUserIds;

        public Harness(Player caller)
        {
            Store = new RedisGuildChatStore(FakeRedis());
            Players.Setup(p => p.FindByIdAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

            var clients = new Mock<IHubCallerClients>();
            clients.Setup(c => c.Caller).Returns(CallerProxy.Object);
            clients.Setup(c => c.Group(It.IsAny<string>()))
                .Callback((string g) => LastGroupName = g)
                .Returns(GroupProxy.Object);
            clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>()))
                .Callback((IReadOnlyList<string> ids) => LastUserIds = ids)
                .Returns(GroupProxy.Object);

            // Default roster: just the caller, when they are in a guild.
            GuildMembers.Setup(r => r.GetForGuildAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(caller.GuildId is null
                    ? new List<GuildMembership>()
                    : new List<GuildMembership>
                      {
                          GuildMembership.Create(caller.GuildId.Value, caller.Id, ROTA.Domain.Enums.GuildRank.Member),
                      });

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
                // The "name" claim carries Player.Username — the hub copies it to SenderUsername (T51).
                new Claim("name", caller.Username),
            });
            ctx.Setup(c => c.User).Returns(new ClaimsPrincipal(identity));
            ctx.Setup(c => c.ConnectionId).Returns("conn-1");

            // Permissive limiter by default — the rate-limit gate has its own dedicated test below.
            RateLimiter.Setup(r => r.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Hub = new ChatHub(Mock.Of<IWorldChatStore>(), Store, Players.Object,
                              Mock.Of<IRaidParticipantRepository>(), RateLimiter.Object,
                              GuildMembers.Object)
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

        // Addressed to the guild's current members, not to a join-time group.
        h.LastUserIds.Should().Contain(member.Id.ToString());
        h.GroupProxy.Verify(p => p.SendCoreAsync("GuildMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        // Landed in that guild's ring buffer with both the display name and the stable username handle.
        var history = await h.Store.GetRecentAsync(guildId, 100);
        var persisted = history.Should().ContainSingle().Subject;
        persisted.Body.Should().Be("hello guild");
        persisted.SenderName.Should().Be(member.DisplayName);
        persisted.SenderUsername.Should().Be(member.Username, "the hub copies Player.Username from the 'name' claim for moderation targeting");
        h.LastCallerEvent.Should().BeNull("a member is not blocked");
    }

    // THE LEAK THIS PINS. A SignalR group is join-time state, and nothing evicted a connection when a
    // membership ended — LeaveGuildChannel resolved the group from Player.GuildId, which leaving has
    // already nulled, so it could not clean up even for an honest client, and a KICKED member was never
    // asked to. The ex-member kept receiving guild chat on the open socket, including the conversation
    // about why they were kicked, and could stack channels by joining and leaving guild after guild.
    //
    // Addressing the CURRENT roster makes membership authoritative at delivery.
    [Fact]
    public async Task SendGuildMessage_IsNotDeliveredToSomeoneWhoHasLeftTheGuild()
    {
        var guildId = Guid.NewGuid();
        var member  = MakePlayer("alice");
        member.JoinGuild(guildId, ROTA.Domain.Enums.GuildRank.Member);
        var exMember = MakePlayer("mallory");   // was in the guild, has since left or been kicked

        var h = new Harness(member);
        // The roster no longer carries the ex-member, even though their socket may still be open and
        // still be in the old SignalR group.
        h.GuildMembers.Setup(r => r.GetForGuildAsync(guildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GuildMembership>
            {
                GuildMembership.Create(guildId, member.Id, ROTA.Domain.Enums.GuildRank.Member),
            });

        await h.Hub.SendGuildMessage("we kicked mallory, here is the plan");

        h.LastUserIds.Should().NotBeNull();
        h.LastUserIds.Should().Contain(member.Id.ToString(), "a current member still receives it");
        h.LastUserIds.Should().NotContain(exMember.Id.ToString(),
            "someone who has left the guild must not keep reading its chat");
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
    public async Task SendGuildMessage_RateLimited_Rejected_NoAppend_NoDbLookup()
    {
        var guildId = Guid.NewGuid();
        var member = MakePlayer("gabe");
        member.JoinGuild(guildId, ROTA.Domain.Enums.GuildRank.Member);
        var h = new Harness(member);
        // Flip the limiter to "over cap" for this caller (exploit-audit finding J).
        h.RateLimiter.Setup(r => r.TryConsumeAsync(member.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await h.Hub.SendGuildMessage("flooding the guild");

        h.LastCallerEvent.Should().Be("RateLimited");
        h.GroupProxy.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
        (await h.Store.GetRecentAsync(guildId, 100)).Should().BeEmpty();
        // Throttled BEFORE the per-message DB lookup — a flood must be cheap to reject.
        h.Players.Verify(p => p.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
