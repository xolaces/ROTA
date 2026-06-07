using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

/// <summary>
/// System 21 Slice 1 — DB-level guild constraints against a real PostgreSQL (Testcontainers):
///   • one ACTIVE guild per player (partial unique index on player_id WHERE is_deleted=false),
///   • re-join after leaving (soft-deleted row must NOT block a fresh membership),
///   • case-insensitive uniqueness of guild Name and Tag (lowercase shadow columns + unique index).
///
/// HOW TO VERIFY THE PARTIAL INDEX IS DOING THE WORK: drop the filter "is_deleted = false" from
/// ix_guild_memberships_player_active in GuildConfigurations and regenerate the migration — the
/// "re-join after leaving" test then fails with a duplicate-key violation. Restore the filter to pass.
/// </summary>
public class GuildMembershipIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_guild_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder().Build();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseContentRoot(FindApiContentRoot());
                host.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                        ["Jwt:PublicKey"] = publicKeyPem,
                        ["Jwt:PrivateKey"] = privateKeyPem,
                        ["Jwt:Issuer"] = "rota-test",
                        ["Jwt:Audience"] = "rota-test",
                        ["Admin:PlayerIds:0"] = Guid.Empty.ToString(),
                        // Lower the creation gates so the test players qualify without big setup.
                        ["GuildConfig:MinCreationLevel"] = "1",
                        ["GuildConfig:CreationGoldCost"] = "0",
                        // Neutralize the startup admin seeder (see BetaKeyConcurrencyTests note).
                        ["Seed:AdminPassword"] = "",
                    });
                });
            });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Player> SeedPlayerAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var p = Player.Create(username, $"{username}@rota.test", "hash");
        db.Players.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>Seeds a real guild row (so membership guild_id FKs are satisfiable) and returns its id.</summary>
    private async Task<Guid> SeedGuildAsync(Guid leaderId, string name, string tag)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var g = Guild.Create(leaderId, name, tag, "seed", GuildJoinPolicy.Open, 50);
        db.Guilds.Add(g);
        await db.SaveChangesAsync();
        return g.Id;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PartialIndex_BlocksSecondActiveMembership_ButAllowsReJoinAfterLeave()
    {
        var player = await SeedPlayerAsync("g_rejoin");
        // Two distinct real guilds so the second insert differs only in guild_id — isolating that the
        // player_id partial unique index (not the guild FK) is what blocks the second active membership.
        var guildA = await SeedGuildAsync(player.Id, "RejoinA", "RJA");
        var guildB = await SeedGuildAsync(player.Id, "RejoinB", "RJB");

        // First active membership inserts cleanly.
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            db.GuildMemberships.Add(GuildMembership.Create(guildA, player.Id, GuildRank.Member));
            await db.SaveChangesAsync();
        }

        // A SECOND active membership for the same player (different guild) must violate the partial unique index.
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            db.GuildMemberships.Add(GuildMembership.Create(guildB, player.Id, GuildRank.Member));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>("one ACTIVE guild membership per player is enforced by the partial unique index");
        }

        // Soft-delete the active membership (the player "leaves").
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var row = await db.GuildMemberships.FirstAsync(m => m.PlayerId == player.Id && !m.IsDeleted);
            row.SoftDelete();
            await db.SaveChangesAsync();
        }

        // A fresh membership now inserts because the lingering soft-deleted row is outside the partial index.
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            db.GuildMemberships.Add(GuildMembership.Create(guildB, player.Id, GuildRank.Member));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().NotThrowAsync("re-joining after leaving must succeed — the soft-deleted row is excluded from the index");
        }

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            (await db.GuildMemberships.CountAsync(m => m.PlayerId == player.Id && !m.IsDeleted)).Should().Be(1);
            (await db.GuildMemberships.CountAsync(m => m.PlayerId == player.Id)).Should().Be(2, "the soft-deleted row lingers alongside the active one");
        }
    }

    [Fact]
    public async Task GuildName_And_Tag_AreUnique_CaseInsensitive()
    {
        var leaderA = await SeedPlayerAsync("g_leaderA");
        var leaderB = await SeedPlayerAsync("g_leaderB");

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            db.Guilds.Add(Guild.Create(leaderA.Id, "Dragons", "DRG", "first", GuildJoinPolicy.Open, 50));
            await db.SaveChangesAsync();
        }

        // Same name, different case → must collide on the normalized unique index.
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            db.Guilds.Add(Guild.Create(leaderB.Id, "dragons", "ZZ", "dup name", GuildJoinPolicy.Open, 50));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>("guild names are unique case-insensitively");
        }

        // Same tag, different case → must collide on the normalized unique index.
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            db.Guilds.Add(Guild.Create(leaderB.Id, "Wyrms", "drg", "dup tag", GuildJoinPolicy.Open, 50));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>("guild tags are unique case-insensitively");
        }
    }

    [Fact]
    public async Task CreateThenDisband_AllowsNameReuse()
    {
        var leaderA = await SeedPlayerAsync("g_reuseA");
        var leaderB = await SeedPlayerAsync("g_reuseB");

        Guid guildId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var g = Guild.Create(leaderA.Id, "Phoenix", "PHX", "first", GuildJoinPolicy.Open, 50);
            db.Guilds.Add(g);
            await db.SaveChangesAsync();
            guildId = g.Id;
        }

        // Disband (soft-delete) the guild.
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var g = await db.Guilds.FirstAsync(x => x.Id == guildId);
            g.Disband();
            await db.SaveChangesAsync();
        }

        // The name/tag are now reusable because the unique indexes are partial on is_deleted=false.
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            db.Guilds.Add(Guild.Create(leaderB.Id, "Phoenix", "PHX", "reborn", GuildJoinPolicy.Open, 50));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().NotThrowAsync("a disbanded guild's name + tag can be reused");
        }
    }

    [Fact]
    public async Task FullJoinFlow_ThroughService_KeepsPlayerInSync()
    {
        // End-to-end through the real service + DB: create (Open), apply→auto-join, leave.
        var leader = await SeedPlayerAsync("g_flow_leader");
        var joiner = await SeedPlayerAsync("g_flow_joiner");

        Guid guildId;
        using (var scope = NewScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGuildService>();
            var create = await svc.CreateGuildAsync(leader.Id, "Vanguard", "VG", "d", GuildJoinPolicy.Open);
            create.Success.Should().BeTrue(create.FailureReason);
            guildId = create.GuildId!.Value;
        }

        using (var scope = NewScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGuildService>();
            (await svc.ApplyAsync(joiner.Id, guildId)).Joined.Should().BeTrue();
        }

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var jp = await db.Players.FirstAsync(p => p.Id == joiner.Id);
            jp.GuildId.Should().Be(guildId);
            jp.GuildRank.Should().Be("Member");
            (await db.Guilds.FirstAsync(g => g.Id == guildId)).MemberCount.Should().Be(2);
        }

        using (var scope = NewScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGuildService>();
            (await svc.LeaveAsync(joiner.Id, guildId)).Success.Should().BeTrue();
        }

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var jp = await db.Players.FirstAsync(p => p.Id == joiner.Id);
            jp.GuildId.Should().BeNull();
            jp.GuildRank.Should().BeNull();
            (await db.Guilds.FirstAsync(g => g.Id == guildId)).MemberCount.Should().Be(1);
        }
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ROTA.Api");
            if (Directory.Exists(Path.Combine(candidate, "content")))
                return candidate;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
