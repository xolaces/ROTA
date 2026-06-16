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

// BETA (System 16 Slice 7) — loop-completion integration tests against the REAL DI container
// (WebApplicationFactory<Program>), so the REAL RaidService, IGauntletService, content providers,
// and Postgres ledgers are exercised. These prove:
//   • a gauntlet ladder-stage raid RESOLVES in combat (HitRaidAsync def lookup) end-to-end and the
//     Slice-4/5 Gauntlet behaviour fires (Strikes spent, GauntletEntry.Score moves, defeat reward),
//   • GetActiveRaidsAsync EXCLUDES gauntlet stages from the regular raid list,
//   • the ladder spawns stage 1 lazily (Personal, GauntletEventId-stamped, correct HP) and returns an
//     existing active stage as-is,
//   • the OpenEvent rank-magic hand-off grants prior winners their consumable for the NEW event,
//     idempotently.
public class GauntletLadderTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_gauntlet_ladder_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder().Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseContentRoot(FindApiContentRoot());
                host.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["ConnectionStrings:Redis"]             = _redis.GetConnectionString(),
                        ["Jwt:PublicKey"]                       = publicKeyPem,
                        ["Jwt:Issuer"]                          = "rota-test",
                        ["Jwt:Audience"]                        = "rota-test",
                        ["Admin:PlayerIds:0"]                   = Guid.Empty.ToString(),
                        ["Seed:AdminPassword"]                  = "",
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

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<Player> SeedPlayerAtLevelAsync(int level)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"g_{suffix}", $"{suffix}@test.dev", "hash");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var stats = scope.ServiceProvider.GetRequiredService<IStatService>();
        while (player.Level < level)
            player.AddExperience(stats.XpToNextLevel(player.Level), lvl => stats.XpToNextLevel(lvl));
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    // Open + activate an event directly (admin service enforces ≤1 active per DB, so each test uses a
    // fresh container DB or closes prior events; these tests run in a single DB so we close as needed).
    private async Task<GauntletEvent> SeedActiveEventAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var ev = GauntletEvent.Create("Cycle", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7));
        ev.Activate();
        db.GauntletEvents.Add(ev);
        await db.SaveChangesAsync();
        return ev;
    }

    private async Task JoinAsync(Guid eventId, Guid playerId, GauntletLeague league)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        db.GauntletEntries.Add(GauntletEntry.Create(eventId, playerId, league));
        await db.SaveChangesAsync();
    }

    private async Task GrantStrikesAsync(Guid playerId, int amount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        // A non-null referenceId would need to be unique; use null (credits need no idempotency key).
        db.StrikeTransactions.Add(StrikeTransaction.Create(
            playerId, amount, StrikeTransactionType.RaidDefeat, referenceId: null));
        await db.SaveChangesAsync();
    }

    // Spawn a gauntlet ladder stage raid directly (mirrors GauntletService.GetLadderAsync's spawn).
    private async Task<ActiveRaid> SeedGauntletStageAsync(Guid eventId, Guid playerId, int stage, long maxHp)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var raid = ActiveRaid.Create(
            $"gauntlet_stage_{stage}", playerId, maxHp,
            DateTimeOffset.UtcNow.AddDays(7), RaidDifficulty.Normal, RaidSize.Personal);
        raid.LinkGauntletEvent(eventId);
        db.ActiveRaids.Add(raid);
        await db.SaveChangesAsync();
        return raid;
    }

    // ── (1) End-to-end: a gauntlet ladder-stage hit RESOLVES + Slice-4/5 behaviour fires ──────────

    [Fact]
    public async Task GauntletStageHit_ResolvesEndToEnd_SpendsStrikes_UpdatesScore()
    {
        var ev     = await SeedActiveEventAsync();
        var player = await SeedPlayerAtLevelAsync(50);
        await JoinAsync(ev.Id, player.Id, GauntletLeague.Whelpling);
        await GrantStrikesAsync(player.Id, 100);

        // A big-HP stage so a single x1 hit does NOT defeat it (keeps the test about resolution+score).
        var raid = await SeedGauntletStageAsync(ev.Id, player.Id, stage: 1, maxHp: 10_000_000L);

        RaidHitResult hit;
        using (var scope = _factory.Services.CreateScope())
        {
            var raids = scope.ServiceProvider.GetRequiredService<IRaidService>();
            // If the gauntlet_stage_1 definition did NOT resolve, HitRaidAsync would throw here
            // (line ~447: `?? throw new InvalidOperationException(...)`). A successful hit proves it does.
            hit = await raids.HitRaidAsync(player.Id, raid.Id, hitSize: 1, Guid.NewGuid().ToString());
        }

        hit.Success.Should().BeTrue("the gauntlet_stage_1 definition resolves in HitRaidAsync");
        hit.Response!.DamageDealt.Should().BeGreaterThan(0);
        // Slice 4 (C): Gauntlet raids spend STRIKES, not stamina. x1 cost = StrikeRatePerSize.Small = 1.
        hit.Response.NewStrikeBalance.Should().Be(99, "one strike spent on the x1 gauntlet hit");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            // Slice 4 (D): the score hook moved GauntletEntry.Score by the hit's damage.
            var entry = await db.GauntletEntries.AsNoTracking()
                .SingleAsync(e => e.GauntletEventId == ev.Id && e.PlayerId == player.Id);
            entry.Score.Should().Be(hit.Response.DamageDealt, "the gauntlet score hook accumulated the hit damage");

            // A strike HitSpend debit row exists (the tx-safe spend committed with the hit).
            var debits = await db.StrikeTransactions.CountAsync(
                t => t.PlayerId == player.Id && t.TransactionType == StrikeTransactionType.HitSpend);
            debits.Should().Be(1);
        }
    }

    [Fact]
    public async Task GauntletStageKill_GrantsDefeatRewards_StrikesAndToken()
    {
        var ev     = await SeedActiveEventAsync();
        var player = await SeedPlayerAtLevelAsync(50);
        await JoinAsync(ev.Id, player.Id, GauntletLeague.Whelpling);
        await GrantStrikesAsync(player.Id, 100);

        // HP = 1 → a single x1 hit kills the stage → the Slice-5 per-defeat reward fires.
        var raid = await SeedGauntletStageAsync(ev.Id, player.Id, stage: 1, maxHp: 1L);

        using (var scope = _factory.Services.CreateScope())
        {
            var raids = scope.ServiceProvider.GetRequiredService<IRaidService>();
            var hit = await raids.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
            hit.Success.Should().BeTrue();
            hit.Response!.IsDefeated.Should().BeTrue("HP=1 stage falls to a single hit");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            // Slice 5: defeat credits +StrikesPerDefeat strikes (default 10) and +1 Token, idempotently.
            var defeatStrikeRows = await db.StrikeTransactions.CountAsync(
                t => t.PlayerId == player.Id && t.TransactionType == StrikeTransactionType.RaidDefeat
                  && t.ReferenceId == $"gauntletdefeat:{raid.Id}:{player.Id}:strikes");
            defeatStrikeRows.Should().Be(1, "the per-defeat strike reward was credited once");

            var tokenRows = await db.GauntletCurrencyTransactions.CountAsync(
                t => t.PlayerId == player.Id && t.Currency == GauntletCurrency.Token
                  && t.ReferenceId == $"gauntletdefeat:{raid.Id}:{player.Id}:token");
            tokenRows.Should().Be(1, "the per-defeat token reward was credited once");
        }
    }

    // ── (3) GetActiveRaidsAsync EXCLUDES gauntlet stages ──────────────────────────────────────────

    [Fact]
    public async Task GetActiveRaids_ExcludesGauntletStages_ButListsRegularOwnRaid()
    {
        var ev     = await SeedActiveEventAsync();
        var player = await SeedPlayerAtLevelAsync(50);
        await JoinAsync(ev.Id, player.Id, GauntletLeague.Whelpling);

        // A gauntlet ladder stage (Personal, owned by the player, event-stamped) — must NOT be listed.
        var gauntletRaid = await SeedGauntletStageAsync(ev.Id, player.Id, stage: 1, maxHp: 5000L);

        // A regular Personal raid owned by the player (no GauntletEventId) — MUST be listed.
        ActiveRaid regular;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            regular = ActiveRaid.Create(
                "raid_ironcolossus", player.Id, maxHp: 5000L,
                DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);
            db.ActiveRaids.Add(regular);
            await db.SaveChangesAsync();
        }

        IReadOnlyList<ActiveRaidResponse> list;
        using (var scope = _factory.Services.CreateScope())
        {
            var raids = scope.ServiceProvider.GetRequiredService<IRaidService>();
            list = await raids.GetActiveRaidsAsync(player.Id);
        }

        list.Should().Contain(r => r.ActiveRaidId == regular.Id, "regular own raids are listed");
        list.Should().NotContain(r => r.ActiveRaidId == gauntletRaid.Id,
            "gauntlet ladder stages are accessed via /api/gauntlet/ladder, not the regular list");
    }

    // ── (2) Ladder spawn via the REAL IGauntletService ───────────────────────────────────────────

    [Fact]
    public async Task GetLadder_FirstCall_SpawnsStage1_SecondCall_ReturnsSameActiveStage()
    {
        var ev     = await SeedActiveEventAsync();
        var player = await SeedPlayerAtLevelAsync(50);
        await JoinAsync(ev.Id, player.Id, GauntletLeague.Whelpling);

        GauntletLadderResponse first;
        using (var scope = _factory.Services.CreateScope())
        {
            var gauntlet = scope.ServiceProvider.GetRequiredService<IGauntletService>();
            first = await gauntlet.GetLadderAsync(player.Id);
        }

        first.CurrentStage.Should().Be(1);
        first.Complete.Should().BeFalse();
        first.JoinedRequired.Should().BeFalse();
        first.NoActiveEvent.Should().BeFalse();
        first.ActiveRaid.Should().NotBeNull();
        first.ActiveRaid!.RaidDefinitionId.Should().Be("gauntlet_stage_1");
        first.ActiveRaid.Size.Should().Be(RaidSize.Personal.ToString());

        // Exactly one gauntlet stage persisted, stamped + Personal + correct HP.
        Guid firstRaidId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var spawned = await db.ActiveRaids.AsNoTracking()
                .Where(r => r.SummonedByPlayerId == player.Id && r.GauntletEventId == ev.Id)
                .ToListAsync();
            spawned.Should().HaveCount(1);
            spawned[0].Size.Should().Be(RaidSize.Personal);
            spawned[0].GauntletEventId.Should().Be(ev.Id);
            spawned[0].MaxHp.Should().Be(5000L, "stage-1 baseHp from gauntlet_raids.json, no difficulty multiplier");
            firstRaidId = spawned[0].Id;
        }

        // Second call → the SAME active stage, no second spawn.
        GauntletLadderResponse second;
        using (var scope = _factory.Services.CreateScope())
        {
            var gauntlet = scope.ServiceProvider.GetRequiredService<IGauntletService>();
            second = await gauntlet.GetLadderAsync(player.Id);
        }

        second.CurrentStage.Should().Be(1);
        second.ActiveRaid!.ActiveRaidId.Should().Be(firstRaidId, "the existing active stage is returned, not re-spawned");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            (await db.ActiveRaids.CountAsync(r => r.SummonedByPlayerId == player.Id && r.GauntletEventId == ev.Id))
                .Should().Be(1, "an already-active stage is never re-spawned");
        }
    }

    // Audit follow-up — the KNOWN ladder double-spawn race. Two (or more) concurrent first GetLadder
    // calls used to both see "no active stage" and both spawn stage 1 → two raids → double per-defeat
    // rewards. The per-player IPlayerMutationLock advisory lock now serializes the decide-and-spawn:
    // the winner spawns, the losers re-query committed truth and return the winner's stage. This test
    // fires N concurrent calls from independent DI scopes (each its own DbContext/connection, so the
    // pg_advisory_xact_lock genuinely contends) and asserts EXACTLY ONE stage-1 raid persisted.
    [Fact]
    public async Task GetLadder_ConcurrentFirstCalls_SpawnsExactlyOneStage()
    {
        var ev     = await SeedActiveEventAsync();
        var player = await SeedPlayerAtLevelAsync(50);
        await JoinAsync(ev.Id, player.Id, GauntletLeague.Whelpling);

        const int concurrency = 8;
        var calls = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            var gauntlet = scope.ServiceProvider.GetRequiredService<IGauntletService>();
            return await gauntlet.GetLadderAsync(player.Id);
        });

        var results = await Task.WhenAll(calls);

        // Every caller observed the SAME single stage 1 (the winner spawned, the losers returned it).
        results.Should().OnlyContain(r => r.CurrentStage == 1 && r.ActiveRaid != null,
            "every concurrent caller resolves the one stage-1 target");
        results.Select(r => r.ActiveRaid!.ActiveRaidId).Distinct().Should().HaveCount(1,
            "all concurrent callers see the same single ladder raid");

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<RotaDbContext>();
        (await db.ActiveRaids.CountAsync(r => r.SummonedByPlayerId == player.Id && r.GauntletEventId == ev.Id))
            .Should().Be(1, "the per-player advisory lock prevents the double-spawn race");
    }

    [Fact]
    public async Task GetLadder_NotJoined_ReturnsJoinedRequired_NoSpawn()
    {
        var ev     = await SeedActiveEventAsync();
        var player = await SeedPlayerAtLevelAsync(50);
        // NOT joined.

        GauntletLadderResponse result;
        using (var scope = _factory.Services.CreateScope())
        {
            var gauntlet = scope.ServiceProvider.GetRequiredService<IGauntletService>();
            result = await gauntlet.GetLadderAsync(player.Id);
        }

        result.JoinedRequired.Should().BeTrue();
        result.ActiveRaid.Should().BeNull();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            (await db.ActiveRaids.CountAsync(r => r.SummonedByPlayerId == player.Id))
                .Should().Be(0, "a non-joined player never spawns a ladder stage");
        }
    }

    // ── (4) OpenEvent rank-magic hand-off ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenEvent_HandsOffRankMagics_FromPriorSettledEvent_Idempotent()
    {
        // A prior event with a rank-1 (Wrath) + a rank-2 (Blessing) winner, settled.
        Guid priorEventId;
        var rank1 = await SeedPlayerAtLevelAsync(50);
        var rank2 = await SeedPlayerAtLevelAsync(50);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var prior = GauntletEvent.Create("Prior", DateTimeOffset.UtcNow.AddDays(-8), DateTimeOffset.UtcNow.AddDays(-1));
            prior.Activate();
            prior.Close();
            db.GauntletEvents.Add(prior);
            var e1 = GauntletEntry.Create(prior.Id, rank1.Id, GauntletLeague.Whelpling);
            e1.AddScore(1000, DateTimeOffset.UtcNow); e1.SetRank(1);
            var e2 = GauntletEntry.Create(prior.Id, rank2.Id, GauntletLeague.Whelpling);
            e2.AddScore(900, DateTimeOffset.UtcNow); e2.SetRank(2);
            db.GauntletEntries.AddRange(e1, e2);
            await db.SaveChangesAsync();
            priorEventId = prior.Id;
        }

        // Settle the prior event (transitions Closed → Settled, stamps SettledAt).
        using (var scope = _factory.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IGauntletAdminService>();
            (await admin.SettleEventAsync(priorEventId)).Success.Should().BeTrue();
        }

        // OPEN a new event → the hand-off grants rank-1 Wrath + rank-2 Blessing on the NEW event.
        GauntletEventResponse newEvent;
        using (var scope = _factory.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IGauntletAdminService>();
            var open = await admin.OpenEventAsync("New Cycle", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
            open.Success.Should().BeTrue();
            newEvent = open.Event!;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            // Rank-1 holds Wrath for the NEW event.
            (await db.PlayerEventMagics.CountAsync(m =>
                m.PlayerId == rank1.Id && m.GauntletEventId == newEvent.Id
                && m.MagicDefinitionId == "magic_wrath_of_the_ancients" && !m.IsDeleted))
                .Should().Be(1, "the prior rank-1 winner becomes the current Wrath owner");
            // Rank-2 holds Blessing for the NEW event.
            (await db.PlayerEventMagics.CountAsync(m =>
                m.PlayerId == rank2.Id && m.GauntletEventId == newEvent.Id
                && m.MagicDefinitionId == "magic_blessing_of_the_ancients" && !m.IsDeleted))
                .Should().Be(1, "the prior rank-2 winner becomes the current Blessing owner");
        }

        // Exactly two consumables on the new event — one per ranked winner, no duplicate rows (the
        // FindAsync pre-check + GrantAsync idempotency held). Re-run idempotency is unit-covered.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            (await db.PlayerEventMagics.CountAsync(m =>
                m.GauntletEventId == newEvent.Id && !m.IsDeleted))
                .Should().Be(2, "exactly two consumables handed off — one per ranked winner, no duplicates");
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
