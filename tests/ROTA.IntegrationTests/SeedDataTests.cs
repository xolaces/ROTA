using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Seeding;

namespace ROTA.IntegrationTests;

/// <summary>
/// Unit-style tests for SeedData.EnsureAdminAsync using a real ServiceCollection
/// but mocked repositories. No DB or containers required.
/// </summary>
public class SeedDataTests
{
    // -----------------------------------------------------------------------
    // FIXTURES
    // -----------------------------------------------------------------------

    private static IServiceProvider BuildProvider(
        IConfiguration config,
        IPlayerRepository players,
        IAuditLogRepository audit)
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(players);
        services.AddSingleton(audit);
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(string? adminPassword, string? adminEmail = null)
    {
        var dict = new Dictionary<string, string?>();
        if (adminPassword is not null)
            dict["Seed:AdminPassword"] = adminPassword;
        if (adminEmail is not null)
            dict["Seed:AdminEmail"] = adminEmail;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // -----------------------------------------------------------------------
    // TESTS
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EnsureAdminAsync_FirstCall_CreatesAdminPlayer()
    {
        var playersMock = new Mock<IPlayerRepository>();
        var auditMock   = new Mock<IAuditLogRepository>();

        // No existing admin.
        playersMock
            .Setup(r => r.UsernameExistsAsync("Xolaces", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        playersMock
            .Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player p, CancellationToken _) => p);

        var sp = BuildProvider(BuildConfig("SuperSecret1!"), playersMock.Object, auditMock.Object);

        await SeedData.EnsureAdminAsync(sp);

        playersMock.Verify(r => r.CreateAsync(
            It.Is<Player>(p =>
                p.Username == "Xolaces" &&
                p.HasRole(PlayerRoles.Admin) &&
                p.HasRole(PlayerRoles.Player) &&
                p.DisplayName == "DEV_Xolaces"),
            It.IsAny<CancellationToken>()), Times.Once,
            "admin must be created with correct username, roles, and display name");

        auditMock.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "AdminSeeded"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureAdminAsync_SecondCall_IsNoOp()
    {
        var playersMock = new Mock<IPlayerRepository>();
        var auditMock   = new Mock<IAuditLogRepository>();

        // Password is configured AND admin already exists — pure no-op.
        playersMock
            .Setup(r => r.UsernameExistsAsync("Xolaces", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sp = BuildProvider(BuildConfig("SuperSecret1!"), playersMock.Object, auditMock.Object);

        await SeedData.EnsureAdminAsync(sp);

        playersMock.Verify(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()),
            Times.Never, "CreateAsync must not be called when the admin already exists");
        auditMock.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureAdminAsync_MissingPassword_DoesNotCreatePlayerAndDoesNotThrow()
    {
        var playersMock = new Mock<IPlayerRepository>();
        var auditMock   = new Mock<IAuditLogRepository>();

        playersMock
            .Setup(r => r.UsernameExistsAsync("Xolaces", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // No Seed:AdminPassword in config.
        var sp = BuildProvider(BuildConfig(adminPassword: null), playersMock.Object, auditMock.Object);

        var act = async () => await SeedData.EnsureAdminAsync(sp);

        await act.Should().NotThrowAsync("missing password must log a warning and return gracefully");
        playersMock.Verify(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()),
            Times.Never, "CreateAsync must not be called when Seed:AdminPassword is missing");
    }

    [Fact]
    public async Task EnsureAdminAsync_CustomEmail_UsedInPlayerCreation()
    {
        var playersMock = new Mock<IPlayerRepository>();
        var auditMock   = new Mock<IAuditLogRepository>();

        playersMock
            .Setup(r => r.UsernameExistsAsync("Xolaces", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        playersMock
            .Setup(r => r.CreateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player p, CancellationToken _) => p);

        var sp = BuildProvider(
            BuildConfig("SuperSecret1!", adminEmail: "custom@rota.dev"),
            playersMock.Object, auditMock.Object);

        await SeedData.EnsureAdminAsync(sp);

        playersMock.Verify(r => r.CreateAsync(
            It.Is<Player>(p => p.Email == "custom@rota.dev"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // T43 — EnsureDevGuildAsync (Dev guild "The Dev Coffee Shop")
    // -----------------------------------------------------------------------

    // In-memory fakes so the orchestration (flag grant + guild create + auto-join) is exercised end-to-end.
    private sealed class FakePlayers : IPlayerRepository
    {
        public readonly List<Player> Players = new();
        public Task<Player?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Players.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        public Task<Player?> FindByUsernameAsync(string username, CancellationToken ct = default)
            => Task.FromResult(Players.FirstOrDefault(p => p.Username == username && !p.IsDeleted));
        public Task UpdateAsync(Player player, CancellationToken ct = default) { if (!Players.Contains(player)) Players.Add(player); return Task.CompletedTask; }
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

    private sealed class FakeGuilds : IGuildRepository
    {
        public readonly List<Guild> Guilds = new();
        public Task<Guild?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Guilds.FirstOrDefault(g => g.Id == id && !g.IsDeleted));
        public Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Guilds.FirstOrDefault(g => g.NameNormalized == Guild.Normalize(name) && !g.IsDeleted));
        public Task<Guild?> FindByTagAsync(string tag, CancellationToken ct = default)
            => Task.FromResult(Guilds.FirstOrDefault(g => g.TagNormalized == Guild.Normalize(tag) && !g.IsDeleted));
        public Task<bool> NameExistsAsync(string name, Guid? excludeGuildId = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> TagExistsAsync(string tag, Guid? excludeGuildId = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<Guild> CreateAsync(Guild guild, CancellationToken ct = default) { if (!Guilds.Contains(guild)) Guilds.Add(guild); return Task.FromResult(guild); }
        public Task UpdateAsync(Guild guild, CancellationToken ct = default) { if (!Guilds.Contains(guild)) Guilds.Add(guild); return Task.CompletedTask; }
        public Task<IReadOnlyList<GuildBrowseEntry>> BrowseAsync(string? query, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GuildBrowseEntry>>(Array.Empty<GuildBrowseEntry>());
        public Task<IReadOnlyList<GuildRosterEntry>> GetRosterAsync(Guid guildId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GuildRosterEntry>>(Array.Empty<GuildRosterEntry>());
    }

    private sealed class FakeMemberships : IGuildMembershipRepository
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

    private sealed class DevHarness
    {
        public FakePlayers Players = new();
        public FakeGuilds Guilds = new();
        public FakeMemberships Memberships = new();
        public Mock<IAuditLogRepository> Audit = new();
        public GuildConfig GuildConfig = new();
        public DeveloperConfig DevConfig = new();
        public IServiceProvider Provider;

        public DevHarness()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IPlayerRepository>(Players);
            services.AddSingleton<IGuildRepository>(Guilds);
            services.AddSingleton<IGuildMembershipRepository>(Memberships);
            services.AddSingleton(Audit.Object);
            services.AddSingleton(Options.Create(GuildConfig));
            services.AddSingleton(Options.Create(DevConfig));
            Provider = services.BuildServiceProvider();
        }

        public Player AddPlayer(string username)
        {
            var p = Player.Create(username, $"{username}@rota.test", "hash");
            Players.Players.Add(p);
            return p;
        }
    }

    [Fact]
    public async Task EnsureDevGuildAsync_EmptyAllowlist_IsNoOp()
    {
        var h = new DevHarness(); // allowlist empty by default
        h.AddPlayer("alice");

        await SeedData.EnsureDevGuildAsync(h.Provider);

        h.Guilds.Guilds.Should().BeEmpty("no dev guild should be created when the allowlist is empty");
        h.Players.Players.Should().OnlyContain(p => !p.HasRole(PlayerRoles.Developer));
        h.Audit.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureDevGuildAsync_GrantsDeveloperFlag_ByUsernameAndByGuid_AndCreatesGuild()
    {
        var h = new DevHarness();
        var nathan = h.AddPlayer("nathan");
        var byGuid = h.AddPlayer("guiduser");
        h.DevConfig.Usernames = new[] { "nathan" };
        h.DevConfig.PlayerIds = new[] { byGuid.Id.ToString() };

        await SeedData.EnsureDevGuildAsync(h.Provider);

        nathan.HasRole(PlayerRoles.Developer).Should().BeTrue("username allowlist entry must be flagged");
        byGuid.HasRole(PlayerRoles.Developer).Should().BeTrue("GUID allowlist entry must be flagged");

        var devGuild = h.Guilds.Guilds.Should().ContainSingle().Subject;
        devGuild.Tag.Should().Be(h.GuildConfig.DevGuildTag);
        devGuild.Name.Should().Be(h.GuildConfig.DevGuildName);
        devGuild.JoinPolicy.Should().Be(GuildJoinPolicy.InviteOnly);

        // First resolvable dev leads; both devs end up in the dev guild.
        nathan.GuildId.Should().Be(devGuild.Id);
        byGuid.GuildId.Should().Be(devGuild.Id);
        h.Memberships.Rows.Should().Contain(m => m.PlayerId == nathan.Id && m.Rank == GuildRank.Leader);
        h.Memberships.Rows.Should().Contain(m => m.PlayerId == byGuid.Id && m.Rank == GuildRank.Member);
        devGuild.MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task EnsureDevGuildAsync_SecondCall_IsIdempotent()
    {
        var h = new DevHarness();
        var nathan = h.AddPlayer("nathan");
        h.DevConfig.Usernames = new[] { "nathan" };

        await SeedData.EnsureDevGuildAsync(h.Provider);
        var guildCountAfterFirst = h.Guilds.Guilds.Count;
        var membershipCountAfterFirst = h.Memberships.Rows.Count;

        await SeedData.EnsureDevGuildAsync(h.Provider);

        h.Guilds.Guilds.Count.Should().Be(guildCountAfterFirst, "no duplicate guild on the second run");
        h.Memberships.Rows.Count.Should().Be(membershipCountAfterFirst, "no duplicate membership on the second run");
        h.Guilds.Guilds.Single().MemberCount.Should().Be(1);
    }

    [Fact]
    public async Task EnsureDevGuildAsync_UnresolvedEntry_IsSkippedNoGuild()
    {
        var h = new DevHarness();
        h.AddPlayer("alice");
        h.DevConfig.Usernames = new[] { "ghost" }; // not a real player

        await SeedData.EnsureDevGuildAsync(h.Provider);

        h.Guilds.Guilds.Should().BeEmpty("no resolvable dev account → skip guild creation");
    }
}
