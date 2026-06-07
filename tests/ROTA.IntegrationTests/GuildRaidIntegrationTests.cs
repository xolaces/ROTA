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
using ROTA.Shared.DTOs;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

/// <summary>
/// System 21 Slice 3b — guild raids against a real PostgreSQL (Testcontainers), exercising the real
/// RaidService.SummonGuildRaidAsync + GuildEconomyRepository.TrySpendPoolAsync so the new DB mechanics
/// are proven:
///   • active_raid.guild_id (column + FK to guilds) is stamped at summon,
///   • the officer-gated summon atomically debits exactly one sigil from the guild pool (raw-SQL,
///     balance-guarded — concurrent summons can never overspend; same discipline as strikes),
///   • the pool empties to InsufficientPool, and non-officers are rejected (no pool spend),
///   • content/guild_raids.json loads cleanly through the real provider/validation at startup.
/// The GuildStamina hit fork + contribution accrual are covered by the RaidService unit tests.
/// </summary>
public class GuildRaidIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_guild_raid_test")
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

    /// <summary>Seeds a guild with a leader + (optional) a plain member, and credits the pool with sigils.</summary>
    private async Task<(Guid leaderId, Guid memberId, Guid guildId)> SeedGuildAsync(
        string keyPrefix, int poolSigils)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var leader = Player.Create($"{keyPrefix}_leader", $"{keyPrefix}_leader@rota.test", "hash");
        var member = Player.Create($"{keyPrefix}_member", $"{keyPrefix}_member@rota.test", "hash");
        db.Players.AddRange(leader, member);

        var guild = Guild.Create(leader.Id, $"{keyPrefix}Guild", $"{keyPrefix[..Math.Min(4, keyPrefix.Length)]}", "seed", GuildJoinPolicy.Open, 50);
        db.Guilds.Add(guild);
        db.GuildMemberships.Add(GuildMembership.Create(guild.Id, leader.Id, GuildRank.Leader));
        db.GuildMemberships.Add(GuildMembership.Create(guild.Id, member.Id, GuildRank.Member));

        if (poolSigils > 0)
            db.GuildSigilPoolTransactions.Add(
                GuildSigilPoolTransaction.Create(guild.Id, poolSigils, GuildSigilPoolTransactionType.Donation, "seed:pool"));

        await db.SaveChangesAsync();
        return (leader.Id, member.Id, guild.Id);
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();
    private IRaidService Raids(IServiceScope s) => s.ServiceProvider.GetRequiredService<IRaidService>();

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Summon_ByLeader_StampsGuildId_AndDebitsExactlyOneSigil()
    {
        var (leaderId, _, guildId) = await SeedGuildAsync("grsummon", poolSigils: 2);

        SummonGuildRaidResult result;
        using (var s = NewScope())
            result = await Raids(s).SummonGuildRaidAsync(leaderId, "guild_raid_warlord", RaidDifficulty.Normal);

        result.Success.Should().BeTrue(result.FailureReason);
        result.PoolBalanceAfter.Should().Be(1, "one sigil was consumed from a pool of two");

        using (var s = NewScope())
        {
            var db = s.ServiceProvider.GetRequiredService<RotaDbContext>();
            var raid = await db.ActiveRaids.SingleOrDefaultAsync(r => r.GuildId == guildId);
            raid.Should().NotBeNull("the summon stamped active_raid.guild_id (column + FK)");
            raid!.Size.Should().Be(RaidSize.Large);
            raid.MaxHp.Should().Be(500000);
            // Pool ledger holds the seed credit (+2) and exactly one summon debit (−1) ⇒ balance 1.
            (await db.GuildSigilPoolTransactions.Where(t => t.GuildId == guildId).SumAsync(t => (long)t.Amount))
                .Should().Be(1);
            (await db.GuildSigilPoolTransactions.CountAsync(
                t => t.GuildId == guildId && t.TransactionType == GuildSigilPoolTransactionType.RaidSummon))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task Summon_DrainsPool_ThenInsufficient()
    {
        var (leaderId, _, _) = await SeedGuildAsync("grdrain", poolSigils: 1);

        using (var s = NewScope())
            (await Raids(s).SummonGuildRaidAsync(leaderId, "guild_raid_warlord", RaidDifficulty.Normal))
                .Success.Should().BeTrue();

        using (var s = NewScope())
        {
            var over = await Raids(s).SummonGuildRaidAsync(leaderId, "guild_raid_warlord", RaidDifficulty.Normal);
            over.Success.Should().BeFalse();
            over.FailureCode.Should().Be(GuildRaidFailureCode.InsufficientPool);
        }
    }

    [Fact]
    public async Task Summon_ByPlainMember_PermissionDenied_NoPoolSpend()
    {
        var (_, memberId, guildId) = await SeedGuildAsync("grperm", poolSigils: 3);

        using (var s = NewScope())
        {
            var result = await Raids(s).SummonGuildRaidAsync(memberId, "guild_raid_warlord", RaidDifficulty.Normal);
            result.Success.Should().BeFalse();
            result.FailureCode.Should().Be(GuildRaidFailureCode.PermissionDenied);
        }

        using (var s = NewScope())
        {
            var db = s.ServiceProvider.GetRequiredService<RotaDbContext>();
            // Pool untouched (still just the +3 seed), no raid created.
            (await db.GuildSigilPoolTransactions.Where(t => t.GuildId == guildId).SumAsync(t => (long)t.Amount))
                .Should().Be(3);
            (await db.ActiveRaids.CountAsync(r => r.GuildId == guildId)).Should().Be(0);
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
