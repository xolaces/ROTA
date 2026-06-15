using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

/// <summary>
/// System 21 Slice 1 — GuildService unit tests. Uses an in-memory fake-backed repository set so the
/// service's orchestration (sync of Player.GuildId/GuildRank, member-count, permission matrix, join
/// flow, succession) is exercised end-to-end without a database. The partial-index uniqueness +
/// one-guild-per-player at the DB level are covered by the integration test.
/// </summary>
public class GuildServiceTests
{
    // ── In-memory fakes ──────────────────────────────────────────────────────

    private sealed class FakeGuildRepo : IGuildRepository
    {
        public readonly List<Guild> Guilds = new();
        public Task<Guild?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Guilds.FirstOrDefault(g => g.Id == id && !g.IsDeleted));
        public Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Guilds.FirstOrDefault(g => g.NameNormalized == Guild.Normalize(name) && !g.IsDeleted));
        public Task<Guild?> FindByTagAsync(string tag, CancellationToken ct = default)
            => Task.FromResult(Guilds.FirstOrDefault(g => g.TagNormalized == Guild.Normalize(tag) && !g.IsDeleted));
        public Task<bool> NameExistsAsync(string name, Guid? excludeGuildId = null, CancellationToken ct = default)
            => Task.FromResult(Guilds.Any(g => g.NameNormalized == Guild.Normalize(name) && !g.IsDeleted && g.Id != excludeGuildId));
        public Task<bool> TagExistsAsync(string tag, Guid? excludeGuildId = null, CancellationToken ct = default)
            => Task.FromResult(Guilds.Any(g => g.TagNormalized == Guild.Normalize(tag) && !g.IsDeleted && g.Id != excludeGuildId));
        public Task<Guild> CreateAsync(Guild guild, CancellationToken ct = default) { if (!Guilds.Contains(guild)) Guilds.Add(guild); return Task.FromResult(guild); }
        public Task UpdateAsync(Guild guild, CancellationToken ct = default) { if (!Guilds.Contains(guild)) Guilds.Add(guild); return Task.CompletedTask; }
        public Task<IReadOnlyList<GuildBrowseEntry>> BrowseAsync(string? query, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GuildBrowseEntry>>(
                Guilds.Where(g => !g.IsDeleted).Select(g => new GuildBrowseEntry { Guild = g, LeaderDisplayName = "leader" }).ToList());
        public Task<IReadOnlyList<GuildRosterEntry>> GetRosterAsync(Guid guildId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GuildRosterEntry>>(Array.Empty<GuildRosterEntry>());
    }

    private sealed class FakeMembershipRepo : IGuildMembershipRepository
    {
        public readonly List<GuildMembership> Rows = new();
        public Task<GuildMembership?> FindByPlayerAsync(Guid playerId, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(m => m.PlayerId == playerId && !m.IsDeleted));
        public Task<GuildMembership?> FindByGuildAndPlayerAsync(Guid guildId, Guid playerId, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(m => m.GuildId == guildId && m.PlayerId == playerId && !m.IsDeleted));
        public Task<IReadOnlyList<GuildMembership>> GetForGuildAsync(Guid guildId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GuildMembership>>(Rows.Where(m => m.GuildId == guildId && !m.IsDeleted).ToList());
        public Task<int> CountActiveAsync(Guid guildId, CancellationToken ct = default)
            => Task.FromResult(Rows.Count(m => m.GuildId == guildId && !m.IsDeleted));
        public Task<GuildMembership> CreateAsync(GuildMembership membership, CancellationToken ct = default) { if (!Rows.Contains(membership)) Rows.Add(membership); return Task.FromResult(membership); }
        public Task UpdateAsync(GuildMembership membership, CancellationToken ct = default) { if (!Rows.Contains(membership)) Rows.Add(membership); return Task.CompletedTask; }
    }

    private sealed class FakeRequestRepo : IGuildJoinRequestRepository
    {
        public readonly List<GuildJoinRequest> Rows = new();
        public Task<GuildJoinRequest?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id && !r.IsDeleted));
        public Task<IReadOnlyList<GuildJoinRequest>> GetPendingForGuildAsync(Guid guildId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GuildJoinRequest>>(Rows.Where(r => r.GuildId == guildId && r.Status == GuildJoinRequestStatus.Pending && !r.IsDeleted).ToList());
        public Task<IReadOnlyList<GuildJoinRequest>> GetPendingForPlayerAsync(Guid playerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GuildJoinRequest>>(Rows.Where(r => r.PlayerId == playerId && r.Status == GuildJoinRequestStatus.Pending && !r.IsDeleted).ToList());
        public Task<GuildJoinRequest?> FindPendingAsync(Guid guildId, Guid playerId, GuildJoinRequestKind kind, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(r => r.GuildId == guildId && r.PlayerId == playerId && r.Kind == kind && r.Status == GuildJoinRequestStatus.Pending && !r.IsDeleted));
        public Task<GuildJoinRequest> CreateAsync(GuildJoinRequest request, CancellationToken ct = default) { if (!Rows.Contains(request)) Rows.Add(request); return Task.FromResult(request); }
        public Task UpdateAsync(GuildJoinRequest request, CancellationToken ct = default) { if (!Rows.Contains(request)) Rows.Add(request); return Task.CompletedTask; }
    }

    private sealed class FakePlayerRepo : IPlayerRepository
    {
        public readonly List<Player> Players = new();
        public Task<Player?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Players.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        public Task<Player?> FindByUsernameAsync(string username, CancellationToken ct = default)
            => Task.FromResult(Players.FirstOrDefault(p => p.Username == username && !p.IsDeleted));
        public Task UpdateAsync(Player player, CancellationToken ct = default) { if (!Players.Contains(player)) Players.Add(player); return Task.CompletedTask; }
        // Unused members for these tests.
        public Task<Player?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<Player?>(null);
        public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default) => Task.FromResult(false);
        public Task<Player> CreateAsync(Player player, CancellationToken ct = default) { Players.Add(player); return Task.FromResult(player); }
        public Task<Player?> FindByIdWithResourcesAsync(Guid id, CancellationToken ct = default) => FindByIdAsync(id, ct);
        public Task<Player?> FindByIdWithStatsAsync(Guid id, CancellationToken ct = default) => FindByIdAsync(id, ct);
        public Task UpdateStatsAsync(PlayerStats stats, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> CountByRoleAsync(PlayerRoles role, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> TryDemoteAdminAsync(Guid targetId, CancellationToken ct = default) => Task.FromResult(false);
        public async Task<TResult> MutateWithRetryAsync<TResult>(Guid playerId, Func<Player, TResult> mutate, CancellationToken ct = default)
        {
            var p = await FindByIdAsync(playerId, ct) ?? throw new InvalidOperationException($"Player {playerId} not found for reward mutation.");
            return mutate(p);
        }
    }

    private sealed class Harness
    {
        public FakeGuildRepo Guilds = new();
        public FakeMembershipRepo Memberships = new();
        public FakeRequestRepo Requests = new();
        public FakePlayerRepo Players = new();
        public Mock<IAuditLogRepository> Audit = new();
        public GuildService Service;

        public Harness(GuildConfig? config = null)
        {
            Service = new GuildService(Guilds, Memberships, Requests, Players, Audit.Object,
                Options.Create(config ?? new GuildConfig()));
        }

        public Player AddPlayer(string username, int level = 30, long gold = 100000)
        {
            var p = Player.Create(username, $"{username}@rota.test", "hash");
            // Bump level/gold via the public domain surface used in app code.
            p.AddExperience(0, _ => int.MaxValue); // no-op level change; keeps Level=1 unless we set below
            SetLevel(p, level);
            p.AddGold(gold);
            Players.Players.Add(p);
            return p;
        }

        // Level has a private setter; the app raises it via AddExperience. For tests, drive it through
        // AddExperience with a trivial curve so the player reaches the desired level.
        private static void SetLevel(Player p, int level)
        {
            if (level <= 1) return;
            // each level needs exactly 1 xp under this curve
            p.AddExperience(level - 1, _ => 1);
        }

        /// <summary>Creates a guild owned by leader and returns it (bypasses gates by giving the leader headroom).</summary>
        public async Task<Guid> Found(Player leader, string name = "Knights", string tag = "KN", GuildJoinPolicy policy = GuildJoinPolicy.Application)
        {
            var r = await Service.CreateGuildAsync(leader.Id, name, tag, "desc", policy);
            r.Success.Should().BeTrue(r.FailureReason);
            return r.GuildId!.Value;
        }

        // ── T43 dev-guild test helpers ──

        /// <summary>Adds a player and flags them as a Developer (mirrors the seed/CLI flag grant).</summary>
        public Player AddDevPlayer(string username, int level = 30, long gold = 100000)
        {
            var p = AddPlayer(username, level, gold);
            p.GrantRole(ROTA.Domain.Enums.PlayerRoles.Developer);
            return p;
        }

        /// <summary>
        /// Seeds the Dev guild ("The Dev Coffee Shop", tag DEV) directly into the fakes, led by
        /// <paramref name="leader"/> (who must be a dev). Bypasses the service create-gate (devs can't create).
        /// </summary>
        public Guid FoundDevGuild(Player leader, GuildConfig config)
        {
            var guild = Guild.Create(leader.Id, config.DevGuildName, config.DevGuildTag,
                config.DevGuildDescription, GuildJoinPolicy.InviteOnly, config.MemberCap);
            Guilds.Guilds.Add(guild);
            Memberships.Rows.Add(GuildMembership.Create(guild.Id, leader.Id, GuildRank.Leader));
            leader.JoinGuild(guild.Id, GuildRank.Leader);
            return guild.Id;
        }
    }

    // ════════════════════════════════════════════════════════════ Create

    [Fact]
    public async Task Create_Succeeds_DeductsGold_SetsLeaderAndMembership()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice", level: 25, gold: 50000);

        var r = await h.Service.CreateGuildAsync(leader.Id, "Knights", "KN", "We fight.", GuildJoinPolicy.Open);

        r.Success.Should().BeTrue(r.FailureReason);
        leader.Gold.Should().Be(50000 - 25000);
        leader.GuildId.Should().Be(r.GuildId);
        leader.GuildRank.Should().Be("Leader");
        h.Memberships.Rows.Should().ContainSingle(m => m.PlayerId == leader.Id && m.Rank == GuildRank.Leader);
        h.Guilds.Guilds.Single().MemberCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_BelowMinLevel_Fails()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice", level: 19, gold: 50000);
        var r = await h.Service.CreateGuildAsync(leader.Id, "Knights", "KN", "d", GuildJoinPolicy.Open);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.InsufficientLevel);
    }

    [Fact]
    public async Task Create_InsufficientGold_Fails()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice", level: 30, gold: 100);
        var r = await h.Service.CreateGuildAsync(leader.Id, "Knights", "KN", "d", GuildJoinPolicy.Open);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.InsufficientGold);
    }

    [Fact]
    public async Task Create_DuplicateName_CaseInsensitive_Fails()
    {
        var h = new Harness();
        var a = h.AddPlayer("alice");
        var b = h.AddPlayer("bob");
        await h.Found(a, "Knights", "KN");

        var r = await h.Service.CreateGuildAsync(b.Id, "kNiGhTs", "ZZ", "d", GuildJoinPolicy.Open);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.NameTaken);
    }

    [Fact]
    public async Task Create_DuplicateTag_CaseInsensitive_Fails()
    {
        var h = new Harness();
        var a = h.AddPlayer("alice");
        var b = h.AddPlayer("bob");
        await h.Found(a, "Knights", "KN");

        var r = await h.Service.CreateGuildAsync(b.Id, "Paladins", "kn", "d", GuildJoinPolicy.Open);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.TagTaken);
    }

    [Fact]
    public async Task Create_ReservedName_Fails()
    {
        var h = new Harness();
        var a = h.AddPlayer("alice");
        var r = await h.Service.CreateGuildAsync(a.Id, "admin", "KN", "d", GuildJoinPolicy.Open);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.Validation);
    }

    [Theory]
    [InlineData("K")]       // too short
    [InlineData("TOOLONG")] // too long (>5)
    public async Task Create_BadTagLength_Fails(string tag)
    {
        var h = new Harness();
        var a = h.AddPlayer("alice");
        var r = await h.Service.CreateGuildAsync(a.Id, "Knights", tag, "d", GuildJoinPolicy.Open);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.Validation);
    }

    [Fact]
    public async Task Create_WhenAlreadyInGuild_Fails()
    {
        var h = new Harness();
        var a = h.AddPlayer("alice");
        await h.Found(a, "Knights", "KN");

        var r = await h.Service.CreateGuildAsync(a.Id, "Paladins", "PL", "d", GuildJoinPolicy.Open);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.AlreadyInGuild);
    }

    // ════════════════════════════════════════════════════════════ Join policies

    [Fact]
    public async Task Apply_OpenGuild_AutoJoinsAsMember()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var joiner = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);

        var r = await h.Service.ApplyAsync(joiner.Id, gid);

        r.Success.Should().BeTrue();
        r.Joined.Should().BeTrue();
        joiner.GuildId.Should().Be(gid);
        joiner.GuildRank.Should().Be("Member");
        h.Guilds.Guilds.Single().MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task Apply_ApplicationGuild_CreatesPending_ThenAcceptMakesMember()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var joiner = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Application);

        var apply = await h.Service.ApplyAsync(joiner.Id, gid);
        apply.Success.Should().BeTrue();
        apply.Joined.Should().BeFalse();
        apply.RequestId.Should().NotBeNull();
        joiner.GuildId.Should().BeNull("an application does not join immediately");

        var accept = await h.Service.AcceptApplicationAsync(leader.Id, gid, apply.RequestId!.Value);
        accept.Success.Should().BeTrue(accept.FailureReason);
        joiner.GuildId.Should().Be(gid);
        joiner.GuildRank.Should().Be("Member");
        h.Requests.Rows.Single().Status.Should().Be(GuildJoinRequestStatus.Accepted);
    }

    [Fact]
    public async Task Apply_ApplicationGuild_DuplicateApplication_IsIdempotent()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var joiner = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Application);

        var first = await h.Service.ApplyAsync(joiner.Id, gid);
        var second = await h.Service.ApplyAsync(joiner.Id, gid);

        second.Success.Should().BeTrue();
        second.RequestId.Should().Be(first.RequestId);
        h.Requests.Rows.Count(r => r.PlayerId == joiner.Id && r.Status == GuildJoinRequestStatus.Pending).Should().Be(1);
    }

    [Fact]
    public async Task Apply_InviteOnlyGuild_RejectsApply()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var joiner = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.InviteOnly);

        var r = await h.Service.ApplyAsync(joiner.Id, gid);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.PolicyForbidsApply);
    }

    [Fact]
    public async Task Invite_ThenAccept_MakesMember()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var invitee = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.InviteOnly);

        var invite = await h.Service.InviteAsync(leader.Id, gid, "bob");
        invite.Success.Should().BeTrue(invite.FailureReason);
        var reqId = h.Requests.Rows.Single(r => r.Kind == GuildJoinRequestKind.Invite).Id;

        var accept = await h.Service.AcceptInviteAsync(invitee.Id, reqId);
        accept.Success.Should().BeTrue(accept.FailureReason);
        invitee.GuildId.Should().Be(gid);
        invitee.GuildRank.Should().Be("Member");
    }

    [Fact]
    public async Task AcceptInvite_ByWrongPlayer_Denied()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var invitee = h.AddPlayer("bob");
        var stranger = h.AddPlayer("carol");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.InviteOnly);
        await h.Service.InviteAsync(leader.Id, gid, "bob");
        var reqId = h.Requests.Rows.Single(r => r.Kind == GuildJoinRequestKind.Invite).Id;

        var r = await h.Service.AcceptInviteAsync(stranger.Id, reqId);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.PermissionDenied);
    }

    [Fact]
    public async Task MemberCap_Enforced_OnOpenJoin()
    {
        var h = new Harness(new GuildConfig { MemberCap = 1 }); // leader fills the only slot
        var leader = h.AddPlayer("alice");
        var joiner = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);

        var r = await h.Service.ApplyAsync(joiner.Id, gid);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.MemberCapReached);
    }

    // ════════════════════════════════════════════════════════════ Permission matrix

    [Fact]
    public async Task Officer_CanKickMember_ButNotOfficerOrLeader()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var officer = h.AddPlayer("bob");
        var member = h.AddPlayer("carol");
        var officer2 = h.AddPlayer("dave");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(officer.Id, gid);
        await h.Service.ApplyAsync(member.Id, gid);
        await h.Service.ApplyAsync(officer2.Id, gid);
        (await h.Service.PromoteAsync(leader.Id, gid, officer.Id)).Success.Should().BeTrue();
        (await h.Service.PromoteAsync(leader.Id, gid, officer2.Id)).Success.Should().BeTrue();

        // Officer kicks a member → OK.
        var kickMember = await h.Service.KickAsync(officer.Id, gid, member.Id);
        kickMember.Success.Should().BeTrue(kickMember.FailureReason);
        member.GuildId.Should().BeNull();

        // Officer cannot kick another officer (equal rank).
        var kickOfficer = await h.Service.KickAsync(officer.Id, gid, officer2.Id);
        kickOfficer.Success.Should().BeFalse();
        kickOfficer.FailureCode.Should().Be(GuildFailureCode.PermissionDenied);

        // Officer cannot kick the leader (higher rank).
        var kickLeader = await h.Service.KickAsync(officer.Id, gid, leader.Id);
        kickLeader.Success.Should().BeFalse();
        kickLeader.FailureCode.Should().Be(GuildFailureCode.PermissionDenied);
    }

    [Fact]
    public async Task OnlyLeader_CanPromote_OfficerCannotCreateOfficer()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var officer = h.AddPlayer("bob");
        var member = h.AddPlayer("carol");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(officer.Id, gid);
        await h.Service.ApplyAsync(member.Id, gid);
        (await h.Service.PromoteAsync(leader.Id, gid, officer.Id)).Success.Should().BeTrue();

        // Officer tries to promote a member to officer → denied (resulting rank Officer is NOT < actor Officer).
        var byOfficer = await h.Service.PromoteAsync(officer.Id, gid, member.Id);
        byOfficer.Success.Should().BeFalse();
        byOfficer.FailureCode.Should().Be(GuildFailureCode.PermissionDenied);
        member.GuildRank.Should().Be("Member");

        // Leader promotes the member to officer → OK + sync.
        var byLeader = await h.Service.PromoteAsync(leader.Id, gid, member.Id);
        byLeader.Success.Should().BeTrue(byLeader.FailureReason);
        member.GuildRank.Should().Be("Officer");
        h.Memberships.Rows.Single(m => m.PlayerId == member.Id).Rank.Should().Be(GuildRank.Officer);
    }

    [Fact]
    public async Task Leader_CanDemoteOfficer_ToMember()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var officer = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(officer.Id, gid);
        await h.Service.PromoteAsync(leader.Id, gid, officer.Id);

        var demote = await h.Service.DemoteAsync(leader.Id, gid, officer.Id);
        demote.Success.Should().BeTrue(demote.FailureReason);
        officer.GuildRank.Should().Be("Member");
    }

    [Fact]
    public async Task Member_CannotSetMotd_OfficerCan()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var officer = h.AddPlayer("bob");
        var member = h.AddPlayer("carol");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(officer.Id, gid);
        await h.Service.ApplyAsync(member.Id, gid);
        await h.Service.PromoteAsync(leader.Id, gid, officer.Id);

        var byMember = await h.Service.UpdateGuildAsync(member.Id, gid, null, null, null, "hi", null);
        byMember.Success.Should().BeFalse();
        byMember.FailureCode.Should().Be(GuildFailureCode.PermissionDenied);

        var byOfficer = await h.Service.UpdateGuildAsync(officer.Id, gid, null, null, null, "welcome", null);
        byOfficer.Success.Should().BeTrue(byOfficer.FailureReason);
        h.Guilds.Guilds.Single().Motd.Should().Be("welcome");
    }

    [Fact]
    public async Task OnlyLeader_CanRename()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var officer = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(officer.Id, gid);
        await h.Service.PromoteAsync(leader.Id, gid, officer.Id);

        var byOfficer = await h.Service.UpdateGuildAsync(officer.Id, gid, "NewName", null, null, null, null);
        byOfficer.Success.Should().BeFalse();
        byOfficer.FailureCode.Should().Be(GuildFailureCode.PermissionDenied);

        var byLeader = await h.Service.UpdateGuildAsync(leader.Id, gid, "NewName", null, null, null, null);
        byLeader.Success.Should().BeTrue(byLeader.FailureReason);
        h.Guilds.Guilds.Single().Name.Should().Be("NewName");
    }

    // ════════════════════════════════════════════════════════════ Leader lifecycle

    [Fact]
    public async Task Leader_CannotLeave()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);

        var r = await h.Service.LeaveAsync(leader.Id, gid);
        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.LeaderCannotLeave);
        leader.GuildId.Should().Be(gid);
    }

    [Fact]
    public async Task Member_CanLeave_DecrementsCount_ClearsPlayer()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var member = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(member.Id, gid);
        h.Guilds.Guilds.Single().MemberCount.Should().Be(2);

        var r = await h.Service.LeaveAsync(member.Id, gid);
        r.Success.Should().BeTrue();
        member.GuildId.Should().BeNull();
        member.GuildRank.Should().BeNull();
        h.Guilds.Guilds.Single().MemberCount.Should().Be(1);
        h.Memberships.Rows.Single(m => m.PlayerId == member.Id).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task TransferLeadership_SwapsRanks()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var member = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(member.Id, gid);

        var r = await h.Service.TransferLeadershipAsync(leader.Id, gid, member.Id);
        r.Success.Should().BeTrue(r.FailureReason);

        h.Guilds.Guilds.Single().LeaderId.Should().Be(member.Id);
        member.GuildRank.Should().Be("Leader");
        leader.GuildRank.Should().Be("Officer");
        h.Memberships.Rows.Single(m => m.PlayerId == member.Id).Rank.Should().Be(GuildRank.Leader);
        h.Memberships.Rows.Single(m => m.PlayerId == leader.Id).Rank.Should().Be(GuildRank.Officer);
    }

    [Fact]
    public async Task Disband_LeaderOnly_ReleasesMembers()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var member = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(member.Id, gid);

        // Member cannot disband.
        var byMember = await h.Service.DisbandGuildAsync(member.Id, gid);
        byMember.Success.Should().BeFalse();
        byMember.FailureCode.Should().Be(GuildFailureCode.PermissionDenied);

        // Leader disbands → guild soft-deleted, all players released.
        var byLeader = await h.Service.DisbandGuildAsync(leader.Id, gid);
        byLeader.Success.Should().BeTrue(byLeader.FailureReason);
        h.Guilds.Guilds.Single().IsDeleted.Should().BeTrue();
        leader.GuildId.Should().BeNull();
        member.GuildId.Should().BeNull();
        h.Memberships.Rows.Should().OnlyContain(m => m.IsDeleted);
    }

    // ════════════════════════════════════════════════════════════ Succession

    [Fact]
    public async Task InactivitySuccession_PromotesMostActiveOfficer()
    {
        var h = new Harness(new GuildConfig { LeaderInactivityDays = 14 });
        var leader = h.AddPlayer("alice");
        var officerStale = h.AddPlayer("bob");
        var officerActive = h.AddPlayer("carol");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(officerStale.Id, gid);
        await h.Service.ApplyAsync(officerActive.Id, gid);
        await h.Service.PromoteAsync(leader.Id, gid, officerStale.Id);
        await h.Service.PromoteAsync(leader.Id, gid, officerActive.Id);

        // Make the leader inactive (>14 days), and give officerActive the more recent activity.
        var leaderM = h.Memberships.Rows.Single(m => m.PlayerId == leader.Id);
        SetLastActive(leaderM, DateTimeOffset.UtcNow.AddDays(-30));
        SetLastActive(h.Memberships.Rows.Single(m => m.PlayerId == officerStale.Id), DateTimeOffset.UtcNow.AddDays(-10));
        SetLastActive(h.Memberships.Rows.Single(m => m.PlayerId == officerActive.Id), DateTimeOffset.UtcNow.AddMinutes(-5));

        var r = await h.Service.RunInactivitySuccessionAsync(gid);
        r.Success.Should().BeTrue(r.FailureReason);

        h.Guilds.Guilds.Single().LeaderId.Should().Be(officerActive.Id);
        officerActive.GuildRank.Should().Be("Leader");
        leader.GuildRank.Should().Be("Officer");
        officerStale.GuildRank.Should().Be("Officer");
    }

    [Fact]
    public async Task InactivitySuccession_LeaderStillActive_NoChange()
    {
        var h = new Harness(new GuildConfig { LeaderInactivityDays = 14 });
        var leader = h.AddPlayer("alice");
        var officer = h.AddPlayer("bob");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);
        await h.Service.ApplyAsync(officer.Id, gid);
        await h.Service.PromoteAsync(leader.Id, gid, officer.Id);

        // Leader is active (membership created just now).
        var r = await h.Service.RunInactivitySuccessionAsync(gid);
        r.Success.Should().BeFalse();
        h.Guilds.Guilds.Single().LeaderId.Should().Be(leader.Id);
    }

    // ════════════════════════════════════════════════════════════ T43 — Dev guild gates

    [Fact]
    public async Task Create_ByDeveloperAccount_RejectedDevGuildRestricted()
    {
        var h = new Harness();
        var dev = h.AddDevPlayer("dev_one");

        var r = await h.Service.CreateGuildAsync(dev.Id, "Knights", "KN", "d", GuildJoinPolicy.Open);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.DevGuildRestricted);
        dev.GuildId.Should().BeNull();
    }

    [Fact]
    public async Task Apply_DeveloperToNormalGuild_RejectedDevGuildRestricted()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var dev = h.AddDevPlayer("dev_one");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.Open);

        var r = await h.Service.ApplyAsync(dev.Id, gid);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.DevGuildRestricted);
        dev.GuildId.Should().BeNull();
    }

    [Fact]
    public async Task Apply_NonDeveloperToDevGuild_RejectedDevGuildRestricted()
    {
        var config = new GuildConfig();
        var h = new Harness(config);
        var devLeader = h.AddDevPlayer("dev_one");
        var outsider = h.AddPlayer("bob");
        var gid = h.FoundDevGuild(devLeader, config);

        var r = await h.Service.ApplyAsync(outsider.Id, gid);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.DevGuildRestricted);
        outsider.GuildId.Should().BeNull();
    }

    [Fact]
    public async Task AcceptInvite_DeveloperToNormalGuild_RejectedDevGuildRestricted()
    {
        var h = new Harness();
        var leader = h.AddPlayer("alice");
        var dev = h.AddDevPlayer("dev_one");
        var gid = await h.Found(leader, policy: GuildJoinPolicy.InviteOnly);

        // An officer of a normal guild crafts an invite directly (the dev isn't yet blocked from receiving
        // an invite row). Accept must enforce the boundary on the invite's guild.
        var req = GuildJoinRequest.Create(gid, dev.Id, GuildJoinRequestKind.Invite);
        h.Requests.Rows.Add(req);

        var r = await h.Service.AcceptInviteAsync(dev.Id, req.Id);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.DevGuildRestricted);
        dev.GuildId.Should().BeNull();
    }

    [Fact]
    public async Task Invite_NonDeveloperIntoDevGuild_RejectedDevGuildRestricted()
    {
        var config = new GuildConfig();
        var h = new Harness(config);
        var devLeader = h.AddDevPlayer("dev_one");
        var outsider = h.AddPlayer("bob");
        var gid = h.FoundDevGuild(devLeader, config);

        var r = await h.Service.InviteAsync(devLeader.Id, gid, "bob");

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.DevGuildRestricted);
        h.Requests.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptApplication_NonDeveloperIntoDevGuild_RejectedDevGuildRestricted()
    {
        var config = new GuildConfig();
        var h = new Harness(config);
        var devLeader = h.AddDevPlayer("dev_one");
        var outsider = h.AddPlayer("bob");
        var gid = h.FoundDevGuild(devLeader, config);

        // A non-dev application row sneaks in (e.g. policy change); accept must still reject it.
        var req = GuildJoinRequest.Create(gid, outsider.Id, GuildJoinRequestKind.Application);
        h.Requests.Rows.Add(req);

        var r = await h.Service.AcceptApplicationAsync(devLeader.Id, gid, req.Id);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildFailureCode.DevGuildRestricted);
        outsider.GuildId.Should().BeNull();
    }

    [Fact]
    public async Task GetGuild_DevGuildByNonDeveloper_ReturnsNull()
    {
        var config = new GuildConfig();
        var h = new Harness(config);
        var devLeader = h.AddDevPlayer("dev_one");
        var outsider = h.AddPlayer("bob");
        var gid = h.FoundDevGuild(devLeader, config);

        var byOutsider = await h.Service.GetGuildAsync(gid, outsider.Id);
        byOutsider.Should().BeNull("the dev guild is invisible to non-devs");

        var byDev = await h.Service.GetGuildAsync(gid, devLeader.Id);
        byDev.Should().NotBeNull("a developer can see the dev guild detail");
    }

    // LastActiveAt has a private setter (the entity exposes TouchActivity for "now"); tests need
    // arbitrary timestamps to simulate inactivity, so reach the private setter via reflection.
    private static void SetLastActive(GuildMembership m, DateTimeOffset when)
    {
        var prop = typeof(GuildMembership).GetProperty(nameof(GuildMembership.LastActiveAt))!;
        prop.GetSetMethod(nonPublic: true)!.Invoke(m, new object[] { when });
    }
}
