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
/// System 21 Slice 3a — guild sigil economy against a real PostgreSQL (Testcontainers), exercising the
/// real GuildEconomyService + GuildEconomyRepository so the DB-level guarantees are proven:
///   • daily claim is idempotent (the unique partial index on (player,currency,type,reference) holds),
///   • empty-ledger balances read back as 0 (SUM-over-no-rows), and grants/spends SUM correctly,
///   • a donation atomically debits the player and credits the guild pool across two tables,
///   • the per-UTC-day buy/donate caps are enforced.
///
/// HOW TO VERIFY THE IDEMPOTENCY INDEX IS DOING THE WORK: drop the "reference_id IS NOT NULL" filter (or
/// the unique flag) from ix_guild_currency_transactions_idempotency and regenerate — the double-claim
/// test then grants twice and fails. Restore it to pass.
/// </summary>
public class GuildSigilEconomyIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_guild_economy_test")
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
                        // Small caps so the cap test is quick and unambiguous.
                        ["GuildConfig:DailyTicketGrantAmount"] = "5",
                        ["GuildConfig:DailyBuyCap"] = "2",
                        ["GuildConfig:DailyDonateCap"] = "2",
                        // Neutralize the startup admin seeder.
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

    /// <summary>Seeds a player who is the leader + member of a fresh guild, returning (playerId, guildId).</summary>
    private async Task<(Guid playerId, Guid guildId)> SeedGuildedPlayerAsync(string username, string name, string tag)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var player = Player.Create(username, $"{username}@rota.test", "hash");
        db.Players.Add(player);
        var guild = Guild.Create(player.Id, name, tag, "seed", GuildJoinPolicy.Open, 50);
        db.Guilds.Add(guild);
        db.GuildMemberships.Add(GuildMembership.Create(guild.Id, player.Id, GuildRank.Leader));
        await db.SaveChangesAsync();
        return (player.Id, guild.Id);
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();
    private IGuildEconomyService Economy(IServiceScope s) => s.ServiceProvider.GetRequiredService<IGuildEconomyService>();

    [Fact]
    public async Task DailyClaim_IsIdempotentAcrossScopes_AndBalancesPersist()
    {
        var (playerId, _) = await SeedGuildedPlayerAsync("eco_claim", "ClaimGuild", "CLM");

        // First claim grants the sigil + ticket allowance.
        using (var s = NewScope())
        {
            var r = await Economy(s).ClaimDailyAsync(playerId);
            r.Success.Should().BeTrue();
            r.AlreadyClaimed.Should().BeFalse();
            r.SigilsGranted.Should().Be(1);
            r.TicketsGranted.Should().Be(5);
        }

        // Second claim the same day — fresh scope (fresh DbContext) — must grant nothing.
        using (var s = NewScope())
        {
            var r = await Economy(s).ClaimDailyAsync(playerId);
            r.Success.Should().BeTrue();
            r.AlreadyClaimed.Should().BeTrue();
            r.SigilsGranted.Should().Be(0);
            r.TicketsGranted.Should().Be(0);
            r.SigilBalance.Should().Be(1);   // proves SUM read-back, not a double-grant
            r.TicketBalance.Should().Be(5);
        }

        // The ledger holds exactly the two original rows.
        using (var s = NewScope())
        {
            var db = s.ServiceProvider.GetRequiredService<RotaDbContext>();
            (await db.GuildCurrencyTransactions.CountAsync(t => t.PlayerId == playerId)).Should().Be(2);
        }
    }

    [Fact]
    public async Task BuyThenDonate_SpendsTickets_MovesSigilToPool_AndEnforcesCaps()
    {
        var (playerId, guildId) = await SeedGuildedPlayerAsync("eco_flow", "FlowGuild", "FLW");

        using (var s = NewScope()) await Economy(s).ClaimDailyAsync(playerId); // 1 sigil, 5 tickets

        // Buy up to the cap (2), then the third buy is rejected.
        using (var s = NewScope()) (await Economy(s).BuySigilAsync(playerId)).Success.Should().BeTrue();
        using (var s = NewScope()) (await Economy(s).BuySigilAsync(playerId)).Success.Should().BeTrue();
        using (var s = NewScope())
        {
            var over = await Economy(s).BuySigilAsync(playerId);
            over.Success.Should().BeFalse();
            over.FailureCode.Should().Be(ROTA.Shared.DTOs.GuildEconomyFailureCode.DailyCapReached);
        }

        // After 2 buys: sigils = 1 (claim) + 2 (buys) = 3; tickets = 5 - 2 = 3.
        using (var s = NewScope())
        {
            var bal = await Economy(s).GetBalancesAsync(playerId);
            bal.SigilBalance.Should().Be(3);
            bal.TicketBalance.Should().Be(3);
            bal.BoughtToday.Should().Be(2);
        }

        // Donate up to the cap (2): personal sigils → guild pool, atomically across two tables.
        using (var s = NewScope()) (await Economy(s).DonateSigilAsync(playerId)).Success.Should().BeTrue();
        using (var s = NewScope())
        {
            var r = await Economy(s).DonateSigilAsync(playerId);
            r.Success.Should().BeTrue();
            r.PoolBalance.Should().Be(2);
        }
        using (var s = NewScope())
        {
            var over = await Economy(s).DonateSigilAsync(playerId);
            over.Success.Should().BeFalse();
            over.FailureCode.Should().Be(ROTA.Shared.DTOs.GuildEconomyFailureCode.DailyCapReached);
        }

        // Final DB truth: player sigils 3 - 2 = 1; pool = 2.
        using (var s = NewScope())
        {
            var db = s.ServiceProvider.GetRequiredService<RotaDbContext>();
            (await db.GuildCurrencyTransactions
                .Where(t => t.PlayerId == playerId && t.Currency == GuildCurrency.Sigil)
                .SumAsync(t => (long)t.Amount)).Should().Be(1);
            (await db.GuildSigilPoolTransactions
                .Where(t => t.GuildId == guildId)
                .SumAsync(t => (long)t.Amount)).Should().Be(2);
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
